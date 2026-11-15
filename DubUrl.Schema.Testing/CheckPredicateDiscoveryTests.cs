#if NET10_0
using DubUrl.Discovery;
using DubUrl.Parser;
using DubUrl.Provider.DuckDb.Discovery;
using DubUrl.Provider.DuckDb.Sql;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;
using DubUrl.Schema.Renderers;
using DuckDB.NET.Data;
using NUnit.Framework;

namespace DubUrl.Schema.Testing;

public class CheckPredicateDiscoveryTests
{
    [Test]
    public async Task WorkOrderCheck_DiscoveryAndRenderedDdlPreserveThePredicate()
    {
        await using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await Execute(connection, """
            CREATE TABLE main."Production.WorkOrder" (
                "StartDate" DATE NOT NULL,
                "EndDate" DATE,
                CHECK (("EndDate" >= "StartDate") OR ("EndDate" IS NULL))
            )
            """);

        var result = await new SchemaInspector().InspectAsync(connection, new DuckDbMetadataReader(), new DiscoveryOptions
        {
            Schemas = ["main"],
            Features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.Checks]
        });

        Assert.That(result.IsComplete, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var table = result.Schema!.Tables.Values.Single();
        var predicate = table.Constraints.Get<PredicateCheckConstraint>()!.Predicate;
        var disjunction = (SqlExpression.BinaryOp)predicate;
        var comparison = (SqlExpression.BinaryOp)disjunction.Left;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(disjunction.Operator, Is.EqualTo(SqlBinaryOperator.Or));
            Assert.That(comparison.Operator, Is.EqualTo(SqlBinaryOperator.GreaterThanOrEqual));
            Assert.That(((SqlExpression.Identifier)comparison.Left).Parts.Single().Name, Is.EqualTo("EndDate"));
            Assert.That(disjunction.Right, Is.TypeOf<SqlExpression.IsNull>());
        }

        await Execute(connection, """DROP TABLE main."Production.WorkOrder" """);
        var dialects = new DialectRegistryBuilder();
        dialects.AddDialect<DuckDbDialect>(["test"]);
        var renderer = new SchemaScriptRenderer(dialects.Build().Get<DuckDbDialect>());
        foreach (var command in renderer.Plan(result.Schema))
            await Execute(connection, command.CommandText);

        await Execute(connection, """
            INSERT INTO main."Production.WorkOrder" VALUES
                (DATE '2026-01-01', NULL),
                (DATE '2026-01-01', DATE '2026-01-01'),
                (DATE '2026-01-01', DATE '2026-01-02')
            """);
        await Assert.ThatAsync(() => Execute(connection, """
            INSERT INTO main."Production.WorkOrder" VALUES (DATE '2026-01-02', DATE '2026-01-01')
            """), Throws.TypeOf<DuckDBException>().With.Message.Contains("CHECK constraint"));
    }

    private static async Task Execute(DuckDBConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
#endif
