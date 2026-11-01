using System.Data;
using DubUrl.Discovery;
using DubUrl.Schema.Constraints;
using NUnit.Framework;

namespace DubUrl.Schema.Testing;

public class SchemaInspectorTests
{
    private static readonly SchemaFeature[] Features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.UniqueConstraints, SchemaFeature.Indexes];

    private static MetadataTable Table() => new(new("sample"),
        [new("first", 0, DbType.Int32, false), new("second", 1, DbType.Int32, false)])
    {
        UniqueConstraints = [new("unique_pair", ["second", "first"]), new("unique_single", ["first"])]
    };

    private static SchemaMetadata Metadata(MetadataTable table, params DiscoveryDiagnostic[] diagnostics)
        => new([table], Features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), diagnostics);

    [TestCase(false)]
    [TestCase(true)]
    public void Map_UniqueConstraints_PreserveNamesColumnOrderAndInstances(bool partial)
    {
        var result = new SchemaInspector().Map(Metadata(Table()), new() { Features = Features, AllowPartial = partial });
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Coverage[SchemaFeature.UniqueConstraints], Is.EqualTo(MetadataCoverage.Available));
        var table = result.Schema!.Tables.Values.Single();
        var unique = table.Constraints.OfType<UniquenessConstraint>().ToArray();
        Assert.That(unique.Select(key => key.Name), Is.EqualTo(new[] { "unique_pair", "unique_single" }));
        Assert.That(unique[0].Columns.Select(column => column.Name), Is.EqualTo(new[] { "second", "first" }));
        Assert.That(unique[1].Columns.Select(column => column.Name), Is.EqualTo(new[] { "first" }));
        foreach (var column in unique.SelectMany(key => key.Columns))
            Assert.That(column, Is.SameAs(table.Columns[column.Name]));
        Assert.That(result.Schema.Indexes, Is.Empty);
    }

    [Test]
    public void Map_UniqueConstraints_LeaveReportedIndexesUnchanged()
    {
        var metadata = Table() with { Indexes = [new(new("real_index"), [new("first", true)], true)] };
        var result = new SchemaInspector().Map(Metadata(metadata), new() { Features = Features });
        Assert.That(result.IsComplete, Is.True);
        var index = result.Schema!.Indexes.Values.Single();
        Assert.That(index.Name, Is.EqualTo("real_index"));
        Assert.That(index.IsUnique, Is.True);
        Assert.That(index.Columns.Keys, Is.EqualTo(new[] { "first" }));
        Assert.That(index.Columns.Values.Single().SortDirection, Is.EqualTo(IndexSortDirection.Descending));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Map_UniqueConstraints_PreserveUnsupportedDiagnosticsAndPartialBehavior(bool partial)
    {
        var diagnostic = new DiscoveryDiagnostic("SDI009", SchemaFeature.Indexes, MetadataCoverage.Unrepresentable,
            "An expression index is not representable.", new("sample"));
        var result = new SchemaInspector().Map(Metadata(Table(), diagnostic), new() { Features = Features, AllowPartial = partial });
        Assert.That(result.IsSuccess, Is.EqualTo(partial));
        Assert.That(result.IsComplete, Is.False);
        Assert.That(result.Diagnostics, Is.EqualTo(new[] { diagnostic }));
        Assert.That(result.Coverage[SchemaFeature.Indexes], Is.EqualTo(MetadataCoverage.Unrepresentable));
        Assert.That(result.Coverage[SchemaFeature.UniqueConstraints], Is.EqualTo(MetadataCoverage.Available));
        Assert.That(result.Schema!.Tables.Values.Single().Constraints.OfType<UniquenessConstraint>().Count(), Is.EqualTo(2));
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("empty")]
    public void Map_InvalidUniqueColumns_FailWithoutLosingSemantics(string kind)
    {
        string[] columns = kind switch { "missing" => ["unknown"], "duplicate" => ["first", "first"], _ => [] };
        var metadata = Table() with { UniqueConstraints = [new("invalid_unique", columns)] };
        var result = new SchemaInspector().Map(Metadata(metadata), new() { Features = Features, AllowPartial = true });
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Schema, Is.Null);
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code), Is.EqualTo(new[] { "SDI007" }));
    }
}
