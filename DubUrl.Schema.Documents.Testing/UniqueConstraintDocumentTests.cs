using System.Data;
using System.Text.Json;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using DubUrl.Schema.Documents.V1;
using Json.Schema;
using NUnit.Framework;

namespace DubUrl.Schema.Documents.Testing;

public class UniqueConstraintDocumentTests
{
    [TestCase(SchemaDocumentFormat.Json)]
    [TestCase(SchemaDocumentFormat.Yaml)]
    public void RoundTrip_PreservesOrderNamesPoliciesAndLegacyFieldUniqueness(SchemaDocumentFormat format)
    {
        var table = new TableBuilder().WithName("pairs").WithColumns(columns => columns
            .Add(column => column.WithName("first").WithType(DbType.Int32))
            .Add(column => column.WithName("second").WithType(DbType.Int32))
            .Add(column => column.WithName("legacy").WithType(DbType.Int32).WithUnique()))
            .WithConstraints(keys => keys
                .AddUnique(key => key.WithColumnNames("second", "first").WithName("pair")
                    .WithNullPolicy(UniqueNullPolicy.NullsDistinct))
                .AddUnique(key => key.WithNullPolicy(UniqueNullPolicy.NullsNotDistinct).WithColumnNames("first"))
                .AddUnique(key => key.WithColumnNames("second").WithName("native_second")))
            .Build();
        var exporter = new SchemaDocumentExporter();
        using var stream = new MemoryStream();
        new SchemaDocumentWriter().Write(stream, exporter.Map(new([table])), format);
        if (format == SchemaDocumentFormat.Json)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "schemas", "schema-document", "v1", "schema.json");
            var schema = JsonSchema.FromText(File.ReadAllText(path), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            using var json = JsonDocument.Parse(stream.ToArray());
            Assert.That(schema.Evaluate(json.RootElement).IsValid, Is.True);
        }

        stream.Position = 0;
        var mapped = new SchemaDocumentMapper().Map(new SchemaDocumentReader().Read(stream, format));
        var result = mapped.Tables.Values.Single();
        var uniques = result.Constraints.OfType<UniquenessConstraint>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(uniques.Select(key => key.Name), Is.EqualTo(new[] { "pair", null, "native_second" }));
            Assert.That(uniques.Select(key => key.NullPolicy),
                Is.EqualTo(new[] { UniqueNullPolicy.NullsDistinct, UniqueNullPolicy.NullsNotDistinct, UniqueNullPolicy.Native }));
            Assert.That(uniques[0].Columns, Is.EqualTo(new[] { result.Columns["second"], result.Columns["first"] }));
            Assert.That(result.Columns["legacy"].Constraints.Get<UniquenessConstraint>()?.NullPolicy, Is.EqualTo(UniqueNullPolicy.Native));
        });
        using var second = new MemoryStream();
        new SchemaDocumentWriter().Write(second, exporter.Map(mapped), format);
        Assert.That(second.ToArray(), Is.EqualTo(stream.ToArray()));
    }

    [Test]
    public void Export_LegacyInlinePrimaryAndNamedUniqueKeepTheirIdentity()
    {
        var id = new Column("id", DbType.Int32, constraints: [new PrimaryKeyConstraint(), new UniquenessConstraint()]);
        var code = new Column("code", DbType.Int32, constraints: [new UniquenessConstraint("code_unique")]);
        var document = new SchemaDocumentExporter().Map(new([new Table("pairs", [id, code])]));
        var mapped = new SchemaDocumentMapper().Map(document).Tables.Values.Single();
        Assert.Multiple(() =>
        {
            Assert.That(mapped.Constraints.Get<PrimaryKeyConstraint>()!.Columns.Keys, Is.EqualTo(new[] { "id" }));
            Assert.That(mapped.Constraints.Get<UniquenessConstraint>()!.Name, Is.EqualTo("code_unique"));
            Assert.That(mapped.Columns["id"].Constraints.Get<UniquenessConstraint>(), Is.Not.Null);
        });
    }

    [Test]
    public void Validate_ReportsMissingRepeatedConflictingAndAmbiguousUniqueDeclarations()
    {
        var table = new TableDocument
        {
            Identity = new() { Name = "pairs" },
            Columns = [new() { Name = "first", Type = "int32", Unique = true }, new() { Name = "second", Type = "int32" }],
            UniqueConstraints =
            [
                new() { Name = "duplicate", Columns = ["second", "second", "missing"] },
                new() { Name = "duplicate", Columns = ["second"], NullPolicy = "invalid" },
                new() { Columns = ["first"], NullPolicy = "nulls-not-distinct" },
                new() { Columns = [] }
            ]
        };
        var document = new SchemaDocument { Version = 1, Tables = [table] };
        var diagnostics = new SchemaDocumentValidator().Validate(document);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[0].columns[1]"));
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[0].columns[2]"));
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[1].name"));
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[1].nullPolicy"));
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[2].nullPolicy"));
            Assert.That(diagnostics.Select(item => item.Path), Does.Contain("tables[0].uniqueConstraints[3].columns"));
            Assert.Throws<SchemaDocumentMappingException>(() => new SchemaDocumentMapper().Map(document));
        });
    }
}
