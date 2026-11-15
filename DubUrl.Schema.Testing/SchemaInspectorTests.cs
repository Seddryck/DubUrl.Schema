using System.Data;
using DubUrl.Discovery;
using DubUrl.Schema.Constraints;
using Moq;
using NUnit.Framework;
using ForeignKeyConstraint = DubUrl.Schema.Constraints.ForeignKeyConstraint;

namespace DubUrl.Schema.Testing;

public class SchemaInspectorTests
{
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task Inspect_RequestsAnnotationsByDefaultOnlyWhenSupported(bool tableSupported, bool columnSupported)
    {
        var reader = new Mock<ISchemaMetadataReader>();
        reader.SetupGet(value => value.Capabilities).Returns(new Dictionary<SchemaFeature, MetadataCoverage>
        {
            [SchemaFeature.Annotations] = tableSupported ? MetadataCoverage.Available : MetadataCoverage.Unsupported,
            [SchemaFeature.ColumnAnnotations] = columnSupported ? MetadataCoverage.Available : MetadataCoverage.Unsupported,
        });
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.SetupGet(value => value.State).Returns(ConnectionState.Open);
        reader.Setup(value => value.ReadAsync(connection.Object, It.IsAny<DiscoveryOptions>(), default))
            .ReturnsAsync((System.Data.Common.DbConnection _, DiscoveryOptions options, CancellationToken _) =>
                new SchemaMetadata([], options.Features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), []));

        var result = await new SchemaInspector().InspectAsync(connection.Object, reader.Object);

        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Coverage.ContainsKey(SchemaFeature.Annotations), Is.EqualTo(tableSupported));
        Assert.That(result.Coverage.ContainsKey(SchemaFeature.ColumnAnnotations), Is.EqualTo(columnSupported));
    }

    [Test]
    public void Map_PreservesTableAndColumnAnnotationsAsImmutableSnapshots()
    {
        var annotations = new Dictionary<string, string> { ["myapp.Source"] = "ERP", ["myapp.source"] = "" };
        var columnAnnotations = new Dictionary<string, string> { ["myapp.source"] = "customer" };
        var column = new MetadataColumn("id", 1, DbType.Int32, false) { Comment = "Identifier", Annotations = columnAnnotations };
        var table = new MetadataTable(new("items"), [column]) { Annotations = annotations };
        SchemaFeature[] features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.Annotations, SchemaFeature.ColumnAnnotations, SchemaFeature.Comments];
        var metadata = new SchemaMetadata([table], features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), []);

        var result = new SchemaInspector().Map(metadata, new() { Features = features });
        annotations.Clear();
        columnAnnotations.Clear();

        Assert.That(result.IsComplete, Is.True);
        var mapped = result.Schema!.Tables.Values.Single();
        Assert.That(mapped.Annotations, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["myapp.Source"] = "ERP",
            ["myapp.source"] = "",
        }));
        Assert.That(mapped.Columns["id"].Description, Is.EqualTo("Identifier"));
        Assert.That(mapped.Columns["id"].Annotations["myapp.source"], Is.EqualTo("customer"));
    }

    [TestCase("complete", false)]
    [TestCase("incomplete", false)]
    [TestCase("incomplete", true)]
    [TestCase("unknown", true)]
    public async Task Inspect_StructuredTypesPreserveCatalogEvidenceWithoutScalarFallback(string state, bool allowPartial)
    {
        var type = state == "unknown" ? null : new StructuredColumnType(
            new ArrayType(new ScalarType(DbType.Int32), state == "complete" ? false : null), StructuredStorage.JsonText);
        var column = new MetadataColumn("payload", 0, DbType.String, true) { Length = 200, NativeType = "VARCHAR" };
        var table = new MetadataTable(new("events", "audit"), [column, new("id", 1, DbType.Int32, false)]);
        SchemaFeature[] features = [SchemaFeature.Tables, SchemaFeature.Columns];
        var options = new DiscoveryOptions { Features = [SchemaFeature.Tables], AllowPartial = allowPartial };
        var metadata = new SchemaMetadata([table], features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), []);
        var reader = new Mock<IStructuredColumnMetadataReader>(MockBehavior.Strict);
        var evidence = new Dictionary<StructuredColumnIdentity, StructuredColumnMetadata>
        {
            [new(table.Identity, column.Name)] = new(type,
                state == "unknown" ? MetadataCoverage.Unrepresentable : MetadataCoverage.Available)
        };
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.SetupGet(value => value.State).Returns(ConnectionState.Open);
        reader.Setup(value => value.ReadWithStructuredTypesAsync(connection.Object, options, default))
            .ReturnsAsync(new StructuredSchemaMetadata(metadata, evidence));

        var result = await new SchemaInspector().InspectAsync(connection.Object, reader.Object, options);
        connection.VerifyGet(value => value.State, Times.Once);
        connection.VerifyNoOtherCalls();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.EqualTo(state == "complete"));
            Assert.That(result.IsSuccess, Is.EqualTo(state == "complete" || (type is not null && allowPartial)));
            Assert.That(result.Coverage[SchemaFeature.Columns],
                Is.EqualTo(state == "complete" ? MetadataCoverage.Available : MetadataCoverage.Unrepresentable));
            if (state != "complete")
                Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("SDI012"));
            if (type is null)
            {
                Assert.That(result.Schema, Is.Null);
                return;
            }
            var columns = result.Schema!.Tables.Values.Single().Columns;
            Assert.That(columns["payload"].StructuredType, Is.SameAs(type));
            Assert.That(columns["payload"].NativeType, Is.Null);
            Assert.That(columns["payload"].Constraints.Get<NullableConstraint>(), Is.Not.Null);
            Assert.That(columns["id"].Type, Is.EqualTo(DbType.Int32));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Inspect_TypedDefaultsPreserveSymbolsAndDiagnoseUnknownWithoutExecutingSql(bool allowPartial)
    {
        var features = new[] { SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.Defaults };
        var sequence = ColumnDefaultExpression.NextSequenceValue(new("sequence.name", "shared space"));
        var table = new MetadataTable(new("records"), [
            new("recorded", 0, DbType.DateTime2, false) { Default = ColumnDefaultExpression.CurrentUtcTimestamp(3) },
            new("scope", 1, DbType.String, true) { Length = 64, Default = ColumnDefaultExpression.CurrentSchema },
            new("user_name", 2, DbType.String, false) { Length = 64, Default = ColumnDefaultExpression.SessionUser },
            new("uuid", 3, DbType.Guid, false) { Default = ColumnDefaultExpression.NewUuid(UuidRepresentation.Native) },
            new("id", 4, DbType.Int64, false) { Default = sequence },
            new("explicit_null", 5, DbType.String, true) { Length = 64, Default = DBNull.Value },
            new("literal_text", 6, DbType.String, false) { Length = 64, Default = "CURRENT_TIMESTAMP" },
            new("unknown", 7, DbType.String, true) { Length = 64, Default = new MetadataExpression("unknown", "arbitrary_sql()") },
            new("incompatible", 8, DbType.Int32, false) { Default = ColumnDefaultExpression.CurrentDatabase }
        ]);
        var coverage = features.ToDictionary(feature => feature, _ => MetadataCoverage.Available);
        var reader = new Mock<ISchemaMetadataReader>(MockBehavior.Strict);
        var connection = new Mock<System.Data.Common.DbConnection>();
        connection.SetupGet(value => value.State).Returns(ConnectionState.Open);
        var options = new DiscoveryOptions { Features = features, AllowPartial = allowPartial };
        reader.Setup(value => value.ReadAsync(connection.Object, options, default)).ReturnsAsync(new SchemaMetadata([table], coverage, []));
        var result = await new SchemaInspector().InspectAsync(connection.Object, reader.Object, options);
        connection.VerifyGet(value => value.State, Times.Once);
        connection.VerifyNoOtherCalls();

        var columns = result.Schema!.Tables.Values.Single().Columns;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.IsSuccess, Is.EqualTo(allowPartial));
            Assert.That(result.Coverage[SchemaFeature.Defaults], Is.EqualTo(MetadataCoverage.Unrepresentable));
            Assert.That(result.Diagnostics.Select(item => item.Code), Is.EqualTo(new[] { "SDI011", "SDI011" }));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("unknown").And.Not.Contain("arbitrary_sql()"));
            Assert.That(columns.Values.Take(5).Select(column => column.DefaultExpression),
                Is.EqualTo(table.Columns.Take(5).Select(column => column.Default)));
            Assert.That(columns["explicit_null"].DefaultValue, Is.SameAs(DBNull.Value));
            Assert.That(columns["literal_text"].DefaultValue, Is.EqualTo("CURRENT_TIMESTAMP"));
            Assert.That(columns["unknown"].DefaultValue, Is.Null);
            Assert.That(columns["incompatible"].DefaultExpression, Is.Null);
        });
    }

    [TestCase(null, "main", null)]
    [TestCase("sales", "store", null)]
    [TestCase("sales", "store", "server")]
    public void Map_NativeNamespaces_PreserveLiteralComponentsAcrossRelationships(string? schema, string? database, string? catalog)
    {
        var parent = new MetadataName("parent.table", schema, database, catalog);
        var child = new MetadataName("child", schema, database, catalog);
        SchemaFeature[] features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.ForeignKeys, SchemaFeature.Indexes, SchemaFeature.Views];
        var metadata = new SchemaMetadata([
            new(parent, [new("id", 0, DbType.Int32, false)])
            { Indexes = [new(new("index.name", schema, database, catalog), [new("id")])] },
            new(child, [new("id", 0, DbType.Int32, false)])
            { ForeignKeys = [new("fk", ["id"], parent, ["id"])] }
        ], features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), [])
        { Views = [new(new("view.name", schema, database, catalog), [], parent, [new(new("column", "id"))])] };
        var result = new SchemaInspector().Map(metadata, new() { Features = features });
        Assert.That(result.IsComplete, Is.True);
        var expected = new DatabaseObjectName(parent.Name, schema, database, catalog);
        Assert.That(result.Schema!.Tables[expected.Key].Identity, Is.EqualTo(expected));
        Assert.That(result.Schema.Tables.Values.Single(table => table.Name == "child").Constraints.OfType<ForeignKeyConstraint>().Single().TargetTable, Is.EqualTo(expected));
        Assert.That(result.Schema.Indexes.Values.Single().TableIdentity, Is.EqualTo(expected));
        Assert.That(result.Schema.Views.Values.Single().Query.Source, Is.EqualTo(expected));
    }

    [TestCase("database")]
    [TestCase("schema")]
    [TestCase("catalog")]
    public void Map_NamespaceFilters_ApplyToTablesViewsAndInventory(string component)
    {
        MetadataName Name(string name, string scope) => component switch
        {
            "database" => new(name, Database: scope), "catalog" => new(name, Catalog: scope), _ => new(name, Schema: scope)
        };
        SchemaFeature[] features = [SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.Views];
        var metadata = new SchemaMetadata(new[] { "literal.scope", "other" }.Select(scope =>
            new MetadataTable(Name("same.table", scope), [new("id", 0, DbType.Int32, false)])).ToArray(),
            features.ToDictionary(feature => feature, _ => MetadataCoverage.Available), [])
        { Views = new[] { "literal.scope", "other" }.Select(scope => new MetadataView(Name("same.view", scope), [], Name("same.table", scope), [new(new("column", "id"))])).ToArray() };
        var options = new DiscoveryOptions { Features = features };
        options = component switch
        {
            "database" => options with { Databases = ["literal.scope"] },
            "catalog" => options with { Catalogs = ["literal.scope"] },
            _ => options with { Schemas = ["literal.scope"] }
        };
        var inspector = new SchemaInspector();
        var result = inspector.Map(metadata, options);
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Schema!.Tables.Count, Is.EqualTo(1));
        Assert.That(result.Schema.Views.Count, Is.EqualTo(1));
        Assert.That(result.ViewInventory.Single().Identity, Is.EqualTo(Name("same.view", "literal.scope")));
        var tableFilter = inspector.Map(metadata, new() { Features = features, Tables = [Name("same.table", "literal.scope")] });
        Assert.That(tableFilter.IsComplete, Is.True);
        Assert.That(tableFilter.Schema!.Tables.Count, Is.EqualTo(1));
        var missing = inspector.Map(metadata, options with { Tables = [Name("same.table", "other")] });
        Assert.That(missing.IsSuccess, Is.False);
        Assert.That(missing.Diagnostics.Single().Code, Is.EqualTo("SDI004"));
    }

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
    public void Map_ExplicitNullPolicy_SuppressesOnlyProviderConfirmedBackingIndex(bool confirmed)
    {
        var backing = new MetadataName("pair_index");
        var metadata = Table() with
        {
            Indexes = [
                new(backing, [new("second"), new("first")], true),
                new(new("other_index"), [new("second")], true)]
        };
        var reader = new UniqueMetadata(confirmed ? backing : null);
        var inspector = new SchemaInspector();
        var result = inspector.Map(Metadata(metadata), new() { Features = Features }, default, reader);
        Assert.That(result.IsComplete, Is.EqualTo(confirmed));
        Assert.That(result.IsSuccess, Is.EqualTo(confirmed));
        Assert.That(result.Schema!.Tables.Values.Single().Constraints.OfType<UniquenessConstraint>().First().NullPolicy,
            Is.EqualTo(UniqueNullPolicy.NullsDistinct));
        Assert.That(result.Schema.Indexes.Count, Is.EqualTo(confirmed ? 1 : 2));
        if (!confirmed)
        {
            Assert.That(result.Diagnostics.Select(item => item.Code), Is.EqualTo(new[] { "SDI010" }));
            Assert.That(result.Coverage[SchemaFeature.Indexes], Is.EqualTo(MetadataCoverage.Unrepresentable));
            var partial = inspector.Map(Metadata(metadata), new() { Features = Features, AllowPartial = true }, default, reader);
            Assert.That(partial.IsSuccess, Is.True);
            Assert.That(partial.IsComplete, Is.False);
        }
    }

    private sealed class UniqueMetadata(MetadataName? backing) : IUniqueConstraintMetadataReader
    {
        public UniqueConstraintMetadata? GetUniqueConstraintMetadata(MetadataTable table, MetadataKey key)
            => key.Name == "unique_pair" ? new(UniqueNullPolicy.NullsDistinct, backing is null ? null : [backing]) : null;
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
