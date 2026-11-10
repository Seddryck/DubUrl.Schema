using DubUrl.Provider.PostgreSql.Sql;
using DubUrl.Provider.Sqlite.Sql;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Documents.V1;
using Json.Schema;
using NUnit.Framework;

namespace DubUrl.Schema.Documents.Testing;

public class SchemaDocumentJsonSchemaTests
{
    private const string SchemaId = "https://seddryck.github.io/DubUrl/schemas/schema-document/v1/schema.json";
    private static readonly string SchemaPath = Path.Combine(
        TestContext.CurrentContext.TestDirectory, "schemas", "schema-document", "v1", "schema.json");
    private static readonly EvaluationOptions EvaluationOptions = new() { OutputFormat = OutputFormat.List };
    private static readonly JsonSchema PublishedSchema = JsonSchema.FromText(File.ReadAllText(SchemaPath));

    private static readonly IReadOnlyDictionary<Type, string?> DtoSchemas = new Dictionary<Type, string?>
    {
        [typeof(SchemaDocument)] = null,
        [typeof(DatabaseObjectNameDocument)] = "databaseObjectName",
        [typeof(TableDocument)] = "table",
        [typeof(ColumnDocument)] = "column",
        [typeof(DefaultExpressionDocument)] = "defaultExpression",
        [typeof(NativeTypeDocument)] = "nativeType",
        [typeof(IdentityColumnDocument)] = "identityColumn",
        [typeof(GeneratedColumnDocument)] = "generatedColumn",
        [typeof(LiteralDocument)] = "literal",
        [typeof(ExpressionDocument)] = "expression",
        [typeof(CheckDocument)] = "check",
        [typeof(PrimaryKeyDocument)] = "primaryKey",
        [typeof(ForeignKeyDocument)] = "foreignKey",
        [typeof(IndexDocument)] = "index",
        [typeof(IndexColumnDocument)] = "indexColumn",
        [typeof(ViewDocument)] = "view",
        [typeof(ViewQueryDocument)] = "viewQuery",
        [typeof(ViewProjectionDocument)] = "viewProjection"
    };

    [Test]
    public void PublishedSchema_IsValidDraft202012SchemaWithStableIdentity()
    {
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(SchemaPath));

        var result = MetaSchemas.Draft202012.Evaluate(schemaDocument.RootElement, EvaluationOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True, JsonSerializer.Serialize(result));
            Assert.That(schemaDocument.RootElement.GetProperty("$schema").GetString(),
                Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
            Assert.That(schemaDocument.RootElement.GetProperty("$id").GetString(), Is.EqualTo(SchemaId));
        });
    }

    [Test]
    public void PublishedSchema_DescribesEveryDtoPropertyAndRejectsUnknownProperties()
    {
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var root = schemaDocument.RootElement;

        Assert.Multiple(() =>
        {
            foreach (var (type, definition) in DtoSchemas)
            {
                var schema = definition is null
                    ? root
                    : root.GetProperty("$defs").GetProperty(definition);
                var actual = schema.GetProperty("properties").EnumerateObject()
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                var expected = type.GetProperties()
                    .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                Assert.That(actual, Is.EqualTo(expected), $"JSON Schema drift for {type.Name}");
                Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False,
                    $"Unknown-property policy for {type.Name}");
                foreach (var property in schema.GetProperty("properties").EnumerateObject())
                {
                    Assert.That(property.Value.TryGetProperty("description", out _), Is.True,
                        $"Missing description for {type.Name}.{property.Name}");
                }
            }
        });
    }

    [TestCase("schema.json")]
    [TestCase("schema.yaml")]
    public void PublishedExamples_ConformToSchemaAndDocumentValidator(string fileName)
    {
        var path = ExamplePath(fileName);
        var source = ReadExampleAsJson(path);
        var schemaResult = Evaluate(source);

        Assert.Multiple(() =>
        {
            Assert.That(schemaResult.IsValid, Is.True,
                $"JSON Schema validation failed for {fileName}: {JsonSerializer.Serialize(schemaResult)}");
            var document = new SchemaDocumentReader().Read(path);
            Assert.That(new SchemaDocumentValidator().Validate(document), Is.Empty,
                $"Document validation failed for {fileName}");
        });
    }

    [TestCase("{\"version\":2}")]
    [TestCase("{\"version\":1,\"unknown\":true}")]
    [TestCase("{\"version\":1,\"views\":[{\"identity\":{\"name\":\"v\"}}]}")]
    [TestCase("{\"version\":1,\"tables\":[{\"identity\":{\"name\":\"t\"},\"columns\":[{\"name\":\"id\",\"type\":\"int32\",\"primaryKey\":true}]}]}")]
    [TestCase("{\"version\":1,\"tables\":[{\"identity\":{\"name\":\"t\"},\"columns\":[{\"name\":\"value\",\"type\":\"string\",\"length\":0}]}]}")]
    [TestCase("{\"version\":1,\"indexes\":[{\"identity\":{\"name\":\"i\"},\"table\":{\"name\":\"t\"},\"columns\":[{\"name\":\"c\",\"direction\":\"sideways\"}]}]}")]
    public void InvalidDocuments_AreRejected(string source)
        => Assert.That(Evaluate(source).IsValid, Is.False);

    [TestCase(SchemaDocumentFormat.Json)]
    [TestCase(SchemaDocumentFormat.Yaml)]
    public void TypedDefaults_RoundTripEveryKind(SchemaDocumentFormat format)
    {
        Column[] columns =
        [
            new("timestamp", DbType.DateTime, defaultExpression: ColumnDefaultExpression.CurrentTimestamp()),
            new("date", DbType.Date, defaultExpression: ColumnDefaultExpression.CurrentDate),
            new("time", DbType.Time, defaultExpression: ColumnDefaultExpression.CurrentTime(0)),
            new("utc_timestamp", DbType.DateTimeOffset, defaultExpression: ColumnDefaultExpression.CurrentUtcTimestamp(6)),
            new VarLengthColumn("current_user", DbType.String, 128, defaultExpression: ColumnDefaultExpression.CurrentUser),
            new VarLengthColumn("session_user", DbType.AnsiString, 128, defaultExpression: ColumnDefaultExpression.SessionUser),
            new VarLengthColumn("current_schema", DbType.String, 128, defaultExpression: ColumnDefaultExpression.CurrentSchema),
            new VarLengthColumn("current_database", DbType.String, 128, defaultExpression: ColumnDefaultExpression.CurrentDatabase),
            new("native_uuid", DbType.Guid, defaultExpression: ColumnDefaultExpression.NewUuid(UuidRepresentation.Native)),
            new VarLengthColumn("binary_uuid", DbType.Binary, 16,
                defaultExpression: ColumnDefaultExpression.NewUuid(UuidRepresentation.Binary, UuidByteOrder.DotNetMixedEndian)),
            new VarLengthColumn("text_uuid", DbType.StringFixedLength, 36,
                defaultExpression: ColumnDefaultExpression.NewUuid(UuidRepresentation.CanonicalText))
        ];
        var schema = new Schema([new Table("samples", columns)]);
        var exporter = new SchemaDocumentExporter();
        var document = exporter.Map(schema);
        Assert.That(document.Tables.Single().Columns.Single(column => column.Name == "current_database").Nullable, Is.False);
        using var stream = new MemoryStream();
        new SchemaDocumentWriter().Write(stream, document, format);
        if (format == SchemaDocumentFormat.Json)
            Assert.That(Evaluate(Encoding.UTF8.GetString(stream.ToArray())).IsValid, Is.True);
        stream.Position = 0;
        var read = new SchemaDocumentReader().Read(stream, format);
        var mapped = new SchemaDocumentMapper().Map(read);
        Assert.That(read.Version, Is.EqualTo(1));
        Assert.That(mapped.Tables.Values.Single().Columns.Values.Select(column => column.DefaultExpression),
            Is.EqualTo(columns.Select(column => column.DefaultExpression)));
        Assert.That(mapped.Tables.Values.Single().Columns.Values.All(column => column.DefaultValue is null && column.Generation is null), Is.True);
    }

    [TestCase("\"type\":\"date\",\"defaultExpression\":{\"kind\":\"arbitrary-sql\"}", SchemaDocumentDiagnosticCodes.InvalidExpression)]
    [TestCase("\"type\":\"date\",\"defaultExpression\":{\"kind\":\"current-date\",\"fractionalSecondsPrecision\":0}", SchemaDocumentDiagnosticCodes.InvalidExpression)]
    [TestCase("\"type\":\"datetime-offset\",\"defaultExpression\":{\"kind\":\"current-utc-timestamp\",\"fractionalSecondsPrecision\":10}", SchemaDocumentDiagnosticCodes.InvalidExpression)]
    [TestCase("\"type\":\"int32\",\"defaultExpression\":{\"kind\":\"session-user\"}", SchemaDocumentDiagnosticCodes.IncompatibleFacet)]
    [TestCase("\"type\":\"date\",\"defaultExpression\":{\"kind\":\"current-schema\"}", SchemaDocumentDiagnosticCodes.IncompatibleFacet)]
    [TestCase("\"type\":\"string\",\"defaultExpression\":{\"kind\":\"current-database\"}", SchemaDocumentDiagnosticCodes.IncompatibleFacet)]
    [TestCase("\"type\":\"string\",\"length\":35,\"defaultExpression\":{\"kind\":\"new-uuid\",\"representation\":\"canonical-text\"}", SchemaDocumentDiagnosticCodes.IncompatibleFacet)]
    [TestCase("\"type\":\"binary\",\"length\":16,\"defaultExpression\":{\"kind\":\"new-uuid\",\"representation\":\"binary\"}", SchemaDocumentDiagnosticCodes.InvalidExpression)]
    [TestCase("\"type\":\"date\",\"default\":{\"type\":\"null\"},\"defaultExpression\":{\"kind\":\"current-date\"}", SchemaDocumentDiagnosticCodes.MutuallyExclusive)]
    [TestCase("\"type\":\"date\",\"generated\":{\"expression\":{\"kind\":\"value\",\"value\":{\"type\":\"null\"}}},\"defaultExpression\":{\"kind\":\"current-date\"}", SchemaDocumentDiagnosticCodes.MutuallyExclusive)]
    public void TypedDefaults_RejectInvalidShapeAndDestinationInSchemaAndValidator(string column, string code)
    {
        var source = $$"""{"version":1,"tables":[{"identity":{"name":"t"},"columns":[{"name":"value",{{column}}}]}]}""";
        Assert.That(Evaluate(source).IsValid, Is.False);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        var document = new SchemaDocumentReader().Read(stream, SchemaDocumentFormat.Json);
        Assert.That(new SchemaDocumentValidator().Validate(document).Select(item => item.Code), Does.Contain(code));
    }

    [TestCase("ansi-string", "sample")]
    [TestCase("string-fixed-length", "sample")]
    [TestCase("date", "2026-11-10")]
    [TestCase("datetime2", "2026-11-10T08:30:00")]
    [TestCase("currency", "9.25")]
    [TestCase("varnumeric", "9.25")]
    public void ExistingV1LiteralDefaults_RetainTheirLogicalType(string type, string value)
    {
        var document = new SchemaDocument { Version = 1, Tables = [
            new() { Identity = new() { Name = "sample" }, Columns = [
                new() { Name = "value", Type = type, Default = new() { Type = type, Value = value } }] }] };
        var mapper = new SchemaDocumentMapper();
        var schema = mapper.Map(document);
        var exported = new SchemaDocumentExporter().Map(schema);
        Assert.That(exported.Tables[0].Columns[0].Default!.Type, Is.EqualTo(type));
        Assert.That(mapper.Map(exported).Tables["sample"].Columns["value"].DefaultValue,
            Is.EqualTo(schema.Tables["sample"].Columns["value"].DefaultValue));
    }

    [Test]
    public void ExistingV1NullLiteralDefault_RoundTripsDistinctFromAnOmittedDefault()
    {
        var document = new SchemaDocument { Version = 1, Tables = [
            new() { Identity = new() { Name = "sample" }, Columns = [
                new() { Name = "explicit_null", Type = "int32", Nullable = true, Default = new() { Type = "null" } },
                new() { Name = "omitted", Type = "int32", Nullable = true }] }] };
        var schema = new SchemaDocumentMapper().Map(document);
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<PgsqlDialect>(["pgsql"]);
        var sql = new SchemaScriptRenderer(builder.Build().Get<PgsqlDialect>()).Plan(schema).Single().CommandText;
        Assert.That(sql, Does.Contain("\"explicit_null\" INTEGER NULL DEFAULT NULL").And.Contain("\"omitted\" INTEGER NULL"));
        var exported = new SchemaDocumentExporter().Map(schema);
        Assert.That(exported.Tables[0].Columns[0].Default!.Type, Is.EqualTo("null"));
        Assert.That(exported.Tables[0].Columns[1].Default, Is.Null);
    }

    [Test]
    public void JsonExample_ReadValidateMapRender_ProducesExpectedPostgresqlScript()
    {
        var document = new SchemaDocumentReader().Read(ExamplePath("schema.json"));
        var domain = new SchemaDocumentMapper().Map(document);
        var dialectBuilder = new DialectRegistryBuilder();
        dialectBuilder.AddDialect<PgsqlDialect>(["pgsql"]);
        var dialects = dialectBuilder.Build();

        var script = new SchemaScriptRenderer(dialects.Get<PgsqlDialect>()).Render(domain);
        var dropScript = new SchemaScriptRenderer(
            dialects.Get<PgsqlDialect>(), SchemaCreationOptions.DropIfExists).Render(domain);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("CONSTRAINT \"pk_customers\" PRIMARY KEY (\"tenant_id\", \"id\")"));
            Assert.That(script, Does.Contain("\"name_length\" INTEGER GENERATED ALWAYS AS (LENGTH(\"name\")) STORED"));
            Assert.That(script, Does.Contain("CONSTRAINT \"ck_orders_amount\" CHECK (\"amount\" >= 0)"));
            Assert.That(script, Does.Contain("CONSTRAINT \"fk_orders_customer\" FOREIGN KEY (\"tenant_id\", \"customer_id\") REFERENCES \"sales\".\"customers\" (\"tenant_id\", \"id\")"));
            Assert.That(script, Does.Contain("CREATE UNIQUE INDEX \"sales\".\"ux_orders_tenant_amount\" ON \"sales\".\"orders\""));
            Assert.That(script, Does.Contain("\"tenant_id\" ASC").And.Contain("\"amount\" DESC"));
            Assert.That(script.IndexOf("CREATE UNIQUE INDEX", StringComparison.Ordinal),
                Is.GreaterThan(script.LastIndexOf("CREATE TABLE", StringComparison.Ordinal)),
                "Indexes must be rendered after table definitions, not inside CREATE TABLE.");
            var firstView = script.IndexOf(
                "CREATE VIEW \"reporting\".\"customer_names\"", StringComparison.Ordinal);
            var secondView = script.IndexOf(
                "CREATE VIEW \"reporting\".\"customer_name_lengths\"", StringComparison.Ordinal);
            Assert.That(firstView, Is.GreaterThan(script.IndexOf("CREATE UNIQUE INDEX", StringComparison.Ordinal)));
            Assert.That(secondView, Is.GreaterThan(firstView));
            Assert.That(script, Does.Contain("LENGTH(\"name\") AS \"name_length\""));

            var secondDrop = dropScript.IndexOf(
                "DROP VIEW IF EXISTS \"reporting\".\"customer_name_lengths\"", StringComparison.Ordinal);
            var firstDrop = dropScript.IndexOf(
                "DROP VIEW IF EXISTS \"reporting\".\"customer_names\"", StringComparison.Ordinal);
            var tableDrop = dropScript.IndexOf("DROP TABLE IF EXISTS", StringComparison.Ordinal);
            Assert.That(secondDrop, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstDrop, Is.GreaterThan(secondDrop));
            Assert.That(tableDrop, Is.GreaterThan(firstDrop));
        });
    }

    [Test]
    public void YamlWithOmittedNullable_ReadMapRender_ProducesNotNullSqliteColumn()
    {
        const string source = """
            version: 1
            tables:
              - identity:
                  name: customer
                columns:
                  - name: is_active
                    type: boolean
                    default:
                      type: boolean
                      value: "true"
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        var document = new SchemaDocumentReader().Read(stream, SchemaDocumentFormat.Yaml);
        var domain = new SchemaDocumentMapper().Map(document);
        var dialectBuilder = new DialectRegistryBuilder();
        dialectBuilder.AddDialect<SqliteDialect>(["sqlite"]);

        var script = new SchemaScriptRenderer(dialectBuilder.Build().Get<SqliteDialect>()).Render(domain);

        Assert.That(script, Does.Contain("\"is_active\" INTEGER NOT NULL DEFAULT true"));
    }

    private static EvaluationResults Evaluate(string source)
    {
        using var instance = JsonDocument.Parse(source);
        return PublishedSchema.Evaluate(instance.RootElement, EvaluationOptions);
    }

    private static string ReadExampleAsJson(string path)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllText(path);

        var document = new SchemaDocumentReader().Read(path);
        return JsonSerializer.Serialize(document, document.GetType(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string ExamplePath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory,
            "schemas", "schema-document", "v1", "examples", fileName);
}
