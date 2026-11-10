using System.Data;
using DubUrl.Mapping;
using DubUrl.Provider.DuckDb.Sql;
using DubUrl.Provider.Oracle.Sql;
using DubUrl.Provider.SqlServer.Sql;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;
using DubUrl.Schema.Renderers;
using Moq;
using NUnit.Framework;

namespace DubUrl.Schema.Testing;

public class SchemaCommandPlanTests
{
    private static IDialect Dialect<T>()
        where T : IDialect
    {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<T>(["test"]);
        return builder.Build().Get<T>();
    }

    [Test]
    public void Plan_SqlServer_PreservesOperationOrderAndGroupsCommentsBeforeSeparateViews()
    {
        var parent = new Table("parent", [new Column("id", DbType.Int32, constraints: [new PrimaryKeyConstraint()])], description: "parent; comment");
        var child = new Table("child", [new Column("id", DbType.Int32, description: "column; 'quoted' -- text")],
            [new Constraints.ForeignKeyConstraint("fk", ["id"], "parent", ["id"])], "child comment");
        var first = new View("v1", new ViewQuery(child.Identity, new ViewProjection(new ColumnIdentityExpression("id"))));
        var second = new View("v2", new ViewQuery(first.Identity, new ViewProjection(new ColumnIdentityExpression("id"))));
        var schema = new Schema([parent, child], [new Index("ix", "child", [new IndexColumn("id")])], [second, first]);
        var renderer = new SchemaScriptRenderer(Dialect<TSqlDialect>(), SchemaCreationOptions.DropIfExists);
        var plan = renderer.Plan(schema);
        Assert.That(plan.Select(c => c.Operation), Is.EqualTo(new[] {
            SchemaSqlOperation.DropView, SchemaSqlOperation.DropView,
            SchemaSqlOperation.DropTable, SchemaSqlOperation.DropTable,
            SchemaSqlOperation.CreateTable, SchemaSqlOperation.CreateTable, SchemaSqlOperation.ForeignKey,
            SchemaSqlOperation.Comment, SchemaSqlOperation.CreateIndex, SchemaSqlOperation.CreateView, SchemaSqlOperation.CreateView }));
        Assert.That(plan.Take(2).Select(c => c.ObjectName), Is.EqualTo(new[] { "v2", "v1" }));
        Assert.That(plan.Skip(2).Take(2).Select(c => c.ObjectName), Is.EqualTo(new[] { "child", "parent" }));
        var comments = plan.Single(c => c.Operation == SchemaSqlOperation.Comment).CommandText;
        Assert.That(comments, Does.Contain("parent; comment").And.Contain("column; ''quoted'' -- text").And.Contain("child comment"));
        Assert.That(plan.TakeLast(2).Select(c => c.ObjectName), Is.EqualTo(new[] { "v1", "v2" }));
        foreach (var view in plan.TakeLast(2)) Assert.That(view.CommandText.TrimStart(), Does.StartWith("CREATE VIEW"));
        // Planning must not alter the established text export.
        Assert.That(renderer.Render(schema), Does.Contain("CREATE VIEW").And.Contain("parent; comment"));
    }

    [Test]
    public void Plan_Oracle_KeepsQuotedSemicolonsAndRemovesOnlyOuterSqlTerminator()
    {
        var table = new Table("table;name", [new VarLengthColumn("value;name", DbType.AnsiString, 30, defaultValue: "a;b'c", description: "column; comment")], description: "table; comment");
        var view = new View("view;name", new ViewQuery(table.Identity, new ViewProjection(new ColumnIdentityExpression("value;name"))));
        var plan = new SchemaScriptRenderer(Dialect<OracleDialect>()).Plan(new([table], [], [view]));
        Assert.That(plan.Select(c => c.Operation), Is.EqualTo(new[] { SchemaSqlOperation.CreateTable, SchemaSqlOperation.Comment, SchemaSqlOperation.Comment, SchemaSqlOperation.CreateView }));
        Assert.That(plan[0].CommandText, Does.Contain("table;name").And.Contain("value;name").And.Contain("a;b''c"));
        Assert.That(plan.All(c => !c.CommandText.TrimEnd().EndsWith(';')), Is.True);
        var block = new SchemaSqlCommand("BEGIN NULL; NULL; END;", SchemaSqlOperation.IdentityPrerequisite, IsProceduralBlock: true);
        var dialect = Dialect<OracleDialect>();
        Assert.That(dialect.SchemaSqlRenderer.PlanCommands(dialect, [block]).Single(), Is.EqualTo(block));
    }

    [Test]
    public void Plan_DuckDb_SeparatesSequenceReplacementBeforeEachTable()
    {
        var schema = new Schema([new Table("generated", [new Column("id", DbType.Int64, generation: new IdentityColumnGeneration(10, 2))])]);
        var renderer = new SchemaScriptRenderer(Dialect<DuckDbDialect>(), SchemaCreationOptions.DropIfExists);
        var plan = renderer.Plan(schema);
        Assert.That(plan.Select(c => c.Operation), Is.EqualTo(new[] { SchemaSqlOperation.DropTable,
            SchemaSqlOperation.IdentityPrerequisite, SchemaSqlOperation.IdentityPrerequisite, SchemaSqlOperation.CreateTable }));
        Assert.That(plan[1].CommandText, Does.StartWith("DROP SEQUENCE"));
        Assert.That(plan[2].CommandText, Does.StartWith("CREATE SEQUENCE").And.Contain("START 10 INCREMENT BY 2"));
        Assert.That(plan[3].CommandText, Does.Not.Contain("CREATE SEQUENCE").And.Contain("nextval"));
        Assert.That(renderer.Render(schema), Does.Contain(plan[1].CommandText + "\r\n" + plan[2].CommandText));
    }

    [Test]
    public void Deploy_Plan_StopsOnFailurePreservesExceptionAndDisposesResources()
    {
        var first = new Mock<IDbCommand>();
        var failed = new Mock<IDbCommand>();
        var error = new InvalidOperationException("provider failure");
        failed.Setup(c => c.ExecuteNonQuery()).Throws(error);
        var connection = new Mock<IDbConnection>();
        connection.SetupSequence(c => c.CreateCommand()).Returns(first.Object).Returns(failed.Object);
        var url = new Mock<ConnectionUrl>("mssql://./db", Mock.Of<ISchemeRegistry>());
        url.Setup(c => c.Open()).Returns(connection.Object);
        var plan = new[] { new SchemaSqlCommand("first;", SchemaSqlOperation.CreateTable, "first"),
            new SchemaSqlCommand("second;", SchemaSqlOperation.CreateTable, "second"),
            new SchemaSqlCommand("never;", SchemaSqlOperation.CreateView, "never") };
        Assert.That(Assert.Throws<InvalidOperationException>(() => new SchemaScriptDeployer().DeploySchema(url.Object, plan)), Is.SameAs(error));
        Assert.That(error.Data["SchemaCommandIndex"], Is.EqualTo(1));
        Assert.That(error.Data["SchemaCommandOperation"], Is.EqualTo("CreateTable"));
        Assert.That(error.Data["SchemaCommandObject"], Is.EqualTo("second"));
        url.Verify(c => c.Open(), Times.Once);
        connection.Verify(c => c.CreateCommand(), Times.Exactly(2));
        first.VerifySet(c => c.CommandText = "first;", Times.Once);
        failed.VerifySet(c => c.CommandText = "second;", Times.Once);
        first.Verify(c => c.Dispose(), Times.Once);
        failed.Verify(c => c.Dispose(), Times.Once);
        connection.Verify(c => c.Dispose(), Times.Once);
    }

    [Test]
    public void Deploy_EmptyOrInvalidPlan_DoesNotOpenConnection()
    {
        var url = new Mock<ConnectionUrl>("mssql://./db", Mock.Of<ISchemeRegistry>());
        var deployer = new SchemaScriptDeployer();
        deployer.DeploySchema(url.Object, Array.Empty<SchemaSqlCommand>());
        Assert.Throws<ArgumentException>(() => deployer.DeploySchema(url.Object, new[] { new SchemaSqlCommand("", SchemaSqlOperation.CreateTable) }));
        url.Verify(c => c.Open(), Times.Never);
    }

    [Test]
    public void Deploy_RenderingFailureInLaterTable_DoesNotOpenConnection()
    {
        var url = new Mock<ConnectionUrl>("mssql://./db", Mock.Of<ISchemeRegistry>());
        url.SetupGet(c => c.Dialect).Returns(Dialect<TSqlDialect>());
        var schema = new Schema([new Table("valid", [new Column("id", DbType.Int32)]),
            new Table("invalid", [new Column("unbounded", DbType.String)])]);
        Assert.Throws<ArgumentException>(() => url.Object.DeploySchema(schema));
        url.Verify(c => c.Open(), Times.Never);
    }

    [Test]
    public void Deploy_UnsupportedTypedDefaultDoesNotOpenConnection()
    {
        var url = new Mock<ConnectionUrl>("mssql://./db", Mock.Of<ISchemeRegistry>());
        url.SetupGet(connection => connection.Dialect).Returns(Dialect<TSqlDialect>());
        var schema = new Schema([new Table("sample", [new Column("created", DbType.DateTime2,
            defaultExpression: ColumnDefaultExpression.CurrentUtcTimestamp())])]);
        Assert.That(Assert.Throws<ColumnDefaultPlanningException>(() => url.Object.DeploySchema(schema))!.Code,
            Is.EqualTo(ColumnDefaultPlanningException.Unsupported));
        url.Verify(connection => connection.Open(), Times.Never);
    }

    [Test]
    public void Deploy_ArbitraryString_IsSubmittedUnchangedAsOneBatch()
    {
        const string sql = "DECLARE @x int = 1; SELECT ';'; -- ;\nSELECT @x;";
        var command = new Mock<IDbCommand>();
        var connection = new Mock<IDbConnection>();
        connection.Setup(c => c.CreateCommand()).Returns(command.Object);
        var url = new Mock<ConnectionUrl>("mssql://./db", Mock.Of<ISchemeRegistry>());
        url.Setup(c => c.Open()).Returns(connection.Object);
        new SchemaScriptDeployer().DeploySchema(url.Object, sql);
        command.VerifySet(c => c.CommandText = sql, Times.Once);
        command.Verify(c => c.ExecuteNonQuery(), Times.Once);
    }
}
