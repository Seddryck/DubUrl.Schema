using DubUrl.Discovery;
using DubUrl.Provider.DuckDb.Discovery;
using DubUrl.Provider.DuckDb.Sql;
using DubUrl.Provider.PostgreSql.Sql;
using DubUrl.Provider.Snowflake.Sql;
using DubUrl.Provider.Sqlite.Sql;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using DubUrl.Schema.Renderers;
using Moq;
using NUnit.Framework;
using DbType = System.Data.DbType;

namespace DubUrl.Schema.Testing;

public class HierarchyTests
{
    [TestCase(null)]
    [TestCase("main")]
    public void DuckDbPreflightAndBothRenderPathsUseAnInlineSelfReference(string? schemaName)
    {
        var schema = Nodes(schemaName: schemaName);
        var renderer = new SchemaScriptRenderer(Dialect<DuckDbDialect>(), SchemaCreationOptions.DropIfExists);

        var plan = renderer.PreflightHierarchies(schema).Single();
        var commands = renderer.Plan(schema);
        var ddl = commands.Single(command => command.Operation == SchemaSqlOperation.CreateTable).CommandText;
        var target = schemaName is null ? "nodes" : $"{schemaName}.nodes";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan, Is.EqualTo(new HierarchyPlan(new("nodes", schemaName), HierarchyRepresentation.ParentId, true, null)));
            Assert.That(ddl, Does.Contain("parent_id BIGINT NULL").And.Contain($"FOREIGN KEY (parent_id) REFERENCES {target} (id)"));
            Assert.That(renderer.Render(schema), Does.Contain(ddl.TrimEnd()));
            Assert.That(commands.Any(command => command.Operation == SchemaSqlOperation.ForeignKey), Is.False);
            Assert.That(schema.Tables.Values.Single().Columns.Keys, Is.EqualTo(new[] { "id" }));
        }
    }

    [Test]
    public void ParentColumnCopiesTypeFacetsWithoutCopyingDefaultsOrConstraints()
    {
        var key = new VarLengthColumn("code", DbType.AnsiString, 30, defaultValue: "root",
            nativeType: new NativeDatabaseType("VARCHAR"));
        var table = new Table(new DatabaseObjectName("nodes"), [key], [new PrimaryKeyConstraint(key)],
            null, null, null, new Hierarchy("code", parentColumnName: "ancestor code"));
        var schema = new Schema([table]);
        var renderer = new SchemaScriptRenderer(Dialect<DuckDbDialect>());

        var plan = renderer.PreflightHierarchies(schema).Single();
        Assert.That(plan.IsSupported, Is.True, plan.Diagnostic);
        var ddl = renderer.Plan(schema).Single(command => command.Operation == SchemaSqlOperation.CreateTable).CommandText;
        Assert.That(ddl, Does.Contain("code VARCHAR(30) DEFAULT 'root'")
            .And.Contain("\"ancestor code\" VARCHAR(30) NULL").And.Not.Contain("\"ancestor code\" VARCHAR(30) NULL DEFAULT"));
    }

    [Test]
    public void PostgreSqlRendersTheSameParentReferenceInAnAlterTableCommand()
    {
        var renderer = new SchemaScriptRenderer(Dialect<PgsqlDialect>());
        var schema = Nodes();

        Assert.That(renderer.PreflightHierarchies(schema).Single().ParentExistenceEnforced, Is.True);
        var command = renderer.Plan(schema).Single(command => command.Operation == SchemaSqlOperation.ForeignKey);
        Assert.That(command.CommandText, Does.Contain("FOREIGN KEY (\"parent_id\") REFERENCES \"nodes\" (\"id\")"));
    }

    [Test]
    public void SqliteCanDeclareTheForeignKeyWithoutClaimingConnectionEnforcement()
    {
        var renderer = new SchemaScriptRenderer(Dialect<SqliteDialect>());

        Assert.That(renderer.PreflightHierarchies(Nodes()).Single().IsSupported, Is.False);
        var schema = Nodes(requireParentExists: false);
        Assert.That(renderer.PreflightHierarchies(schema).Single(),
            Is.EqualTo(new HierarchyPlan(new("nodes"), HierarchyRepresentation.ParentId, false, null)));
        Assert.That(renderer.Render(schema), Does.Contain("FOREIGN KEY (\"parent_id\") REFERENCES \"nodes\" (\"id\")"));
    }

    [Test]
    public void UnsupportedEnforcementIsReportedBeforeDdlAndCanBeExplicitlyRelaxed()
    {
        var renderer = new SchemaScriptRenderer(Dialect<SnowflakeDialect>());
        var schema = Nodes();

        var failure = renderer.PreflightHierarchies(schema).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.IsSupported, Is.False);
            Assert.That(failure.ParentExistenceEnforced, Is.False);
            Assert.That(failure.Diagnostic, Does.Contain("cannot enforce parent existence"));
            Assert.That(() => renderer.Plan(schema), Throws.TypeOf<NotSupportedException>().With.Message.EqualTo(failure.Diagnostic));
            Assert.That(() => renderer.Render(schema), Throws.TypeOf<NotSupportedException>().With.Message.EqualTo(failure.Diagnostic));
        }

        var relaxed = Nodes(requireParentExists: false);
        Assert.That(renderer.PreflightHierarchies(relaxed).Single(),
            Is.EqualTo(new HierarchyPlan(new("nodes"), HierarchyRepresentation.ParentId, false, null)));
        Assert.That(renderer.Render(relaxed), Does.Contain("\"parent_id\"").And.Not.Contain("FOREIGN KEY"));
    }

    [TestCase("missing", false, "single-column primary key")]
    [TestCase("id", true, "already exists")]
    public void PreflightDiagnosesInvalidKeysAndGeneratedNameCollisions(string nodeKey, bool collision, string message)
    {
        var key = new Column("id", DbType.Int64);
        Column[] columns = collision ? [key, new Column("PARENT_ID", DbType.Int64)] : [key];
        var table = new Table(new DatabaseObjectName("nodes"), columns, [new PrimaryKeyConstraint(key)],
            null, null, null, new Hierarchy(nodeKey));
        var renderer = new SchemaScriptRenderer(Dialect<DuckDbDialect>());

        var plan = renderer.PreflightHierarchies(new([table])).Single();

        Assert.That(plan.IsSupported, Is.False);
        Assert.That(plan.Diagnostic, Does.Contain(message));
    }

    [Test]
    public void PreflightUsesTheProvidersPlanAndPassesCreationOptions()
    {
        var dialect = new Mock<IDialect>();
        var sql = new Mock<ISchemaSqlRenderer>();
        var hierarchy = sql.As<IHierarchySqlRenderer>();
        dialect.SetupGet(value => value.SchemaSqlRenderer).Returns(sql.Object);
        var duck = Dialect<DuckDbDialect>();
        dialect.SetupGet(value => value.DbTypeMapper).Returns(duck.DbTypeMapper);
        dialect.SetupGet(value => value.Renderer).Returns(duck.Renderer);
        dialect.SetupGet(value => value.SqlFunctionMapper).Returns(duck.SqlFunctionMapper);
        var path = new Column("path", DbType.Binary, nativeType: new NativeDatabaseType("hierarchyid"));
        hierarchy.Setup(value => value.PlanHierarchy(dialect.Object, It.Is<HierarchyContext>(context =>
                context.NodeKey.Name == "id" && context.Options == SchemaCreationOptions.DropIfExists)))
            .Returns(new HierarchyStoragePlan(HierarchyRepresentation.HierarchyId, false, [path], []));
        sql.Setup(value => value.RenderColumnType(dialect.Object, "HIERARCHYID", null, 0, 0, true))
            .Returns("HIERARCHYID");
        var nativeTypes = new NativeTypeRegistry().Register<IDialect>("hierarchyid");
        var renderer = new SchemaScriptRenderer(dialect.Object, SchemaCreationOptions.DropIfExists, nativeTypes);

        var plan = renderer.PreflightHierarchies(Nodes(requireParentExists: false)).Single();

        Assert.That(plan, Is.EqualTo(new HierarchyPlan(new("nodes"), HierarchyRepresentation.HierarchyId, false, null)));
    }

    [Test]
    public void InspectionPreservesThePhysicalForeignKeyAndOnlyRecoversExplicitDeclarations()
    {
        var identity = new MetadataName("nodes");
        var table = new MetadataTable(identity, [new("id", 1, DbType.Int64, false), new("parent_id", 2, DbType.Int64, true)])
        {
            PrimaryKey = new("pk", ["id"]),
            ForeignKeys = [new("fk", ["parent_id"], identity, ["id"])]
        };
        SchemaFeature[] features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.PrimaryKeys, SchemaFeature.ForeignKeys];
        var metadata = new SchemaMetadata([table], features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), []);
        var options = new DiscoveryOptions { Features = features };
        var inspector = new SchemaInspector();
        var reader = new Mock<IHierarchyMetadataReader>();
        var hierarchy = new Hierarchy("id");
        reader.Setup(value => value.GetHierarchy(table)).Returns(hierarchy);

        var physical = inspector.Map(metadata, options);
        var explicitMetadata = inspector.Map(metadata, options, default, null, null, reader.Object);
        var inspected = explicitMetadata.Schema!.Tables.Values.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(physical.Hierarchies, Is.Empty);
            Assert.That(explicitMetadata.IsComplete, Is.True);
            Assert.That(explicitMetadata.Hierarchies[inspected.Identity], Is.EqualTo(hierarchy));
            Assert.That(inspected.Columns.Keys, Is.EqualTo(new[] { "id", "parent_id" }));
            Assert.That(inspected.Constraints.Get<ForeignKeyConstraint>()!.TargetTable, Is.EqualTo(inspected.Identity));
            Assert.That(inspected.Hierarchy, Is.Null);
        }
    }

#if NET10_0
    [Test]
    public async Task DuckDbGeneratedHierarchyAcceptsRootsAndChildrenAndRejectsMissingParents()
    {
        var renderer = new SchemaScriptRenderer(Dialect<DuckDbDialect>());
        var schema = Nodes(schemaName: "main");
        await using var connection = new DuckDB.NET.Data.DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var command in renderer.Plan(schema))
            await Execute(connection, command.CommandText);

        await Execute(connection, "INSERT INTO main.nodes (id) VALUES (1), (2)");
        await Execute(connection, "INSERT INTO main.nodes (id, parent_id) VALUES (3, 1)");
        await Assert.ThatAsync(() => Execute(connection, "INSERT INTO main.nodes VALUES (4, 99)"),
            Throws.TypeOf<DuckDB.NET.Data.DuckDBException>().With.Message.Contains("foreign key constraint"));

        var inspected = await new SchemaInspector().InspectAsync(connection, new DuckDbMetadataReader(), new DiscoveryOptions
        {
            Features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.PrimaryKeys, SchemaFeature.ForeignKeys],
            Schemas = ["main"]
        });
        Assert.That(inspected.IsComplete, Is.True, string.Join(Environment.NewLine, inspected.Diagnostics));
        var table = inspected.Schema!.Tables.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Columns["parent_id"].Type, Is.EqualTo(DbType.Int64));
            Assert.That(table.Columns["parent_id"].Constraints.Get<NullableConstraint>(), Is.Not.Null);
            Assert.That(table.Constraints.Get<ForeignKeyConstraint>()!.TargetTable, Is.EqualTo(table.Identity));
            Assert.That(inspected.Hierarchies, Is.Empty);
        }
    }

    private static async Task Execute(DuckDB.NET.Data.DuckDBConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
#endif

    private static Schema Nodes(bool requireParentExists = true, string? schemaName = null)
        => new([new TableBuilder()
            .WithIdentity(new("nodes", schemaName))
            .WithColumns(columns => columns.Add(column => column.WithName("id").WithType(DbType.Int64)))
            .WithConstraints(constraints => constraints.AddPrimaryKey(key => key.WithColumnNames("id")))
            .WithHierarchy(new Hierarchy("id", requireParentExists))
            .Build()]);

    private static TDialect Dialect<TDialect>()
        where TDialect : class, IDialect
    {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<TDialect>(["test"]);
        return builder.Build().Get<TDialect>();
    }
}