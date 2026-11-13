using DubUrl.Provider.SqlServer.Sql;
using System.Data;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Casters;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Querying.Dialects.Renderers;
using DubUrl.Querying.TypeMapping;
using DubUrl.Schema.Renderers;
using DubUrl.Schema.Constraints;
using NUnit.Framework;
using ForeignKey = DubUrl.Schema.Constraints.ForeignKeyConstraint;

namespace DubUrl.Schema.Testing;

public class SchemaSqlRendererExtensionTests
{
    private IDialect source = null!;

    [SetUp]
    public void SetUp()
    {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<TSqlDialect>(["mssql"]);
        source = builder.Build().Get<TSqlDialect>();
    }

    [Test]
    public void TableAnnotationsRequireAnExplicitProviderHook()
    {
        var identity = new DatabaseObjectName("annotated");
        var table = new Table(identity, [new("id", DbType.Int32)], null, null, null,
            new Dictionary<string, string> { ["myapp.source"] = "ERP" });
        var schema = new Schema([table]);
        var unsupported = new SchemaScriptRenderer(new ProviderDialect(source, StandardSchemaSqlRenderer.Instance));
        Assert.That(() => unsupported.Render(schema), Throws.TypeOf<NotSupportedException>().With.Message.Contains("annotated"));
        Assert.That(() => unsupported.Plan(schema), Throws.TypeOf<NotSupportedException>().With.Message.Contains("annotated"));

        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, new AnnotationRenderer()));
        var plainTable = new Table("plain", [new("id", DbType.Int32)]);
        var plan = renderer.Plan(new([table, plainTable]));
        Assert.That(plan, Has.Count.EqualTo(3));
        Assert.That(plan[0].Operation, Is.EqualTo(SchemaSqlOperation.CreateTable));
        Assert.That(plan[1].CommandText, Is.EqualTo("ANNOTATE annotated ERP;"));
        Assert.That(plan[2].Operation, Is.EqualTo(SchemaSqlOperation.CreateTable));
    }

    private sealed class AnnotationRenderer : StandardSchemaSqlRenderer, ITableAnnotationSqlRenderer
    {
        public IReadOnlyList<SchemaSqlCommand> PlanTableAnnotations(IDialect dialect, DatabaseObjectName table,
            IReadOnlyDictionary<string, string> annotations)
            => [new($"ANNOTATE {table.Name} {annotations["myapp.source"]};", SchemaSqlOperation.Comment, table.ToString())];
    }

    [Test]
    public void CommandBasedScriptsPreserveCheckNamesAndRunPrerequisitesBeforeReplacement()
    {
        var parent = new Table("external", [new Column("id", DbType.Int32)]);
        var value = new ColumnIdentityExpression("id");
        var child = new Table("child",
            [new Column("id", DbType.Int32, DBNull.Value, [new CheckConstraint(value, ">=", new ValueExpression(0), "positive")])],
            [new ForeignKey("child_fk", ["id"], parent.Identity, ["id"])]);
        var model = new Schema([child], [], [], [parent]);
        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, new CommandRenderer()), SchemaCreationOptions.DropIfExists);

        var plan = renderer.Plan(model);
        Assert.That(plan.Select(command => command.Operation), Is.EqualTo(new[]
        {
            SchemaSqlOperation.ForeignKey, SchemaSqlOperation.DropTable, SchemaSqlOperation.CreateTable,
            SchemaSqlOperation.CreateTable, SchemaSqlOperation.ForeignKey
        }));
        Assert.That(plan[0].CommandText, Is.EqualTo("VERIFY external;"));
        Assert.That(plan[2].CommandText, Does.Contain("DEFAULT NULL").And.Contain(") USING STUB;").And.Not.Contain("CHECK"));
        Assert.That(plan[3].CommandText, Is.EqualTo("ADD positive CHECK ([id] >= 0);"));
        Assert.That(renderer.Render(model), Is.EqualTo(string.Join(Environment.NewLine, plan.Select(command => command.CommandText.TrimEnd()))));
    }

    private sealed class CommandRenderer : StandardSchemaSqlRenderer, ISchemaCommandSqlRenderer
    {
        public string RenderTableOptions(IDialect dialect, bool hasDefaults) => hasDefaults ? " USING STUB" : "";

        public IReadOnlyList<SchemaSqlCommand> PlanTableChecks(IDialect dialect, DatabaseObjectName table,
            IReadOnlyList<SchemaCheckDefinition> checks)
            => checks.Select(check => new SchemaSqlCommand($"ADD {check.Name} CHECK ({check.Predicate});",
                SchemaSqlOperation.CreateTable, table.ToString())).ToArray();

        public IReadOnlyList<SchemaSqlCommand> PlanForeignKeyPrerequisites(IDialect dialect, ForeignKey key,
            TableDefinitionContext? createdTarget)
            => createdTarget is null ? [new($"VERIFY {key.TargetTable};", SchemaSqlOperation.ForeignKey)] : [];
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StructuredTypes_PassStorageAndShapeToBothColumnRenderingPaths(bool providerOwnsColumn)
    {
        var type = new StructuredColumnType(new StructType([new("field.name", new VectorType(DbType.Single, 3), false)]),
            StructuredStorage.JsonText);
        var column = new Column("payload", type, DBNull.Value, [new NullableConstraint()]);
        var table = new Table(new DatabaseObjectName("events", "audit"), [column, new("id", DbType.Int32)]);
        var provider = new StructuredRenderer("STUB_TYPE([field.name], 3)", providerOwnsColumn);
        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, provider));
        var schema = new Schema([table]);

        var sql = renderer.Plan(schema).Single().CommandText;
        var context = provider.Contexts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(context.Table, Is.EqualTo(table.Identity));
            Assert.That(context.Column, Is.SameAs(column));
            Assert.That(context.Type, Is.SameAs(type));
            Assert.That(context.Nullable, Is.True);
            Assert.That(sql, Does.Contain("[payload] STUB_TYPE([field.name], 3) NULL DEFAULT NULL"));
            Assert.That(renderer.Render(schema), Does.Contain("[payload] STUB_TYPE([field.name], 3) NULL DEFAULT NULL"));
        });
    }

    [TestCase("missing hook")]
    [TestCase("unsupported storage")]
    [TestCase("incomplete SQL")]
    [TestCase("incomplete shape")]
    public void StructuredTypes_RejectUnsupportedAndIncompletePlans(string failure)
    {
        var type = new StructuredColumnType(new ArrayType(new ScalarType(DbType.Int32),
            failure == "incomplete shape" ? null : false), StructuredStorage.Binary);
        var schema = new Schema([new Table("events", [new("payload", type)])]);
        var provider = new StructuredRenderer(failure == "unsupported storage" ? null : " ");
        var renderer = new SchemaScriptRenderer(failure == "missing hook" ? source : new ProviderDialect(source, provider),
            SchemaCreationOptions.DropIfExists);

        Assert.That(() => renderer.Plan(schema), Throws.TypeOf<NotSupportedException>().With.Message.Contains("events.payload"));
        Assert.That(() => renderer.Render(schema), Throws.TypeOf<NotSupportedException>());
        if (failure == "incomplete shape")
            Assert.That(provider.Contexts, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StructuredTypes_ValidateForeignKeyEndpointsWithTableContext(bool external)
    {
        var type = new StructuredColumnType(new JsonType(), StructuredStorage.JsonText);
        var targetColumn = new Column("key", type);
        var parent = new Table(new DatabaseObjectName("parent", "audit"), [targetColumn],
            [new PrimaryKeyConstraint([targetColumn])]);
        var child = new Table("child", [new Column("ref", type)],
            [new ForeignKey("fk", ["ref"], parent.Identity, ["key"])]);
        var schema = external ? new Schema([child], [], [], [parent]) : new Schema([parent, child]);
        var provider = new StructuredRenderer("STUB_JSON_TEXT");

        var plan = new SchemaScriptRenderer(new ProviderDialect(source, provider)).Plan(schema);

        Assert.That(provider.Contexts, Has.Count.EqualTo(2));
        Assert.That(provider.Contexts, Has.All.Property(nameof(StructuredColumnTypeContext.IsKey)).True);
        Assert.That(provider.Contexts.Single(context => context.Table == parent.Identity).Nullable, Is.False);
        Assert.That(plan.Count(command => command.Operation == SchemaSqlOperation.CreateTable), Is.EqualTo(external ? 1 : 2));
    }

    private sealed class StructuredRenderer(string? sql, bool ownsColumn = false)
        : StandardSchemaSqlRenderer, IStructuredColumnSqlRenderer, IColumnDefinitionSqlRenderer
    {
        public List<StructuredColumnTypeContext> Contexts { get; } = [];

        public string? RenderStructuredType(IDialect dialect, StructuredColumnTypeContext context)
        {
            Contexts.Add(context);
            return sql;
        }

        public ColumnDefinitionPlan? RenderColumnDefinition(IDialect dialect, ColumnDefinitionContext column)
            => ownsColumn ? new(column.Type, ColumnDefinitionClauses.None) : null;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TypedDefaults_UseStructuredContextAndRenderOnceThroughEitherColumnPath(bool providerOwnsDefault)
    {
        var tableIdentity = new DatabaseObjectName("records", "ddl_schema", "ddl_database");
        var column = new Column("recorded", DbType.DateTimeOffset,
            defaultExpression: ColumnDefaultExpression.CurrentUtcTimestamp(3));
        var provider = new DefaultPlanningRenderer((_, _) => new("STUB_UTC(3)"), providerOwnsDefault);
        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, provider));
        var schema = new Schema([new Table(tableIdentity, [column,
            new Column("omitted", DbType.Int32), new Column("value", DbType.Int32, 7),
            new Column("explicit_null", DbType.Int32, DBNull.Value)])]);

        var sql = renderer.Plan(schema).Single().CommandText;
        var integerType = source.DbTypeMapper.ToDictionary()[nameof(DbType.Int32)];
        Assert.Multiple(() =>
        {
            Assert.That(provider.Contexts.Single().Column, Is.SameAs(column));
            Assert.That(provider.Contexts.Single().Table, Is.EqualTo(tableIdentity));
            Assert.That(provider.Contexts.Single().SqlType, Does.Contain("DATETIMEOFFSET").IgnoreCase);
            Assert.That(sql, Does.Contain("[recorded] DATETIMEOFFSET DEFAULT STUB_UTC(3)").IgnoreCase);
            Assert.That(sql, Does.Contain($"[omitted] {integerType},").And.Contain($"[value] {integerType} DEFAULT 7")
                .And.Contain($"[explicit_null] {integerType} DEFAULT NULL").IgnoreCase);
            Assert.That(sql.Split("STUB_UTC(3)").Length, Is.EqualTo(2));
            Assert.That(renderer.Render(schema), Does.Contain("DEFAULT STUB_UTC(3)"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContextDefaults_PreserveDistinctSymbolsAndAcceptExplicitAliases(bool alias)
    {
        var provider = new DefaultPlanningRenderer((_, context) => context.Column.DefaultExpression!.Kind switch
        {
            ColumnDefaultKind.CurrentSchema => alias
                ? new("STUB_DATABASE()", 64, true, ColumnDefaultKind.CurrentDatabase)
                : new("STUB_SCHEMA()", 64, true),
            ColumnDefaultKind.CurrentDatabase => new("STUB_DATABASE()", 64, true),
            ColumnDefaultKind.CurrentUser => new("STUB_EFFECTIVE_USER()", 64, false),
            ColumnDefaultKind.SessionUser => new("STUB_SESSION_USER()", 64, false),
            _ => null
        });
        var table = new Table(new DatabaseObjectName("records", "ddl_schema", "ddl_database"),
            [ContextColumn("scope", ColumnDefaultExpression.CurrentSchema), ContextColumn("db", ColumnDefaultExpression.CurrentDatabase),
             ContextColumn("effective_user", ColumnDefaultExpression.CurrentUser), ContextColumn("session_user", ColumnDefaultExpression.SessionUser)]);
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, provider)).Plan(new([table])).Single().CommandText;
        Assert.That(sql, Does.Contain($"DEFAULT STUB_{(alias ? "DATABASE" : "SCHEMA")}()")
            .And.Contain("DEFAULT STUB_DATABASE()").And.Contain("DEFAULT STUB_EFFECTIVE_USER()").And.Contain("DEFAULT STUB_SESSION_USER()"));
        Assert.That(provider.Contexts.Select(context => context.Column.DefaultExpression!.Kind),
            Is.EqualTo(new[] { ColumnDefaultKind.CurrentSchema, ColumnDefaultKind.CurrentDatabase, ColumnDefaultKind.CurrentUser, ColumnDefaultKind.SessionUser }));
    }

    [TestCase("no hook", ColumnDefaultPlanningException.Unsupported)]
    [TestCase("unresolved", ColumnDefaultPlanningException.Unsupported)]
    [TestCase("capacity", ColumnDefaultPlanningException.Unsupported)]
    [TestCase("nullability", ColumnDefaultPlanningException.Unsupported)]
    [TestCase("primary key", ColumnDefaultPlanningException.Unsupported)]
    [TestCase("missing capacity", ColumnDefaultPlanningException.Incomplete)]
    [TestCase("missing nullability", ColumnDefaultPlanningException.Incomplete)]
    [TestCase("blank SQL", ColumnDefaultPlanningException.Incomplete)]
    [TestCase("wrong alias", ColumnDefaultPlanningException.Incomplete)]
    public void TypedDefaults_RejectUnsupportedOrIncompletePlans(string reason, string code)
    {
        var column = ContextColumn("scope", ColumnDefaultExpression.CurrentSchema, nullable: reason != "nullability");
        var table = new Table("records", [column], reason == "primary key" ? [new PrimaryKeyConstraint([column])] : []);
        var provider = new DefaultPlanningRenderer((_, _) => reason switch
        {
            "unresolved" => null,
            "capacity" => new("STUB_SCHEMA()", 65, false),
            "missing capacity" => new("STUB_SCHEMA()", MayReturnNull: false),
            "missing nullability" => new("STUB_SCHEMA()", RequiredTextCapacity: 64),
            "blank SQL" => new(" ", 64, false),
            "wrong alias" => new("STUB_SCHEMA()", 64, false, ColumnDefaultKind.CurrentUser),
            _ => new("STUB_SCHEMA()", 64, true)
        });
        var renderer = new SchemaScriptRenderer(reason == "no hook" ? source : new ProviderDialect(source, provider));
        var schema = new Schema([table]);
        Assert.That(Assert.Throws<ColumnDefaultPlanningException>(() => renderer.Plan(schema))!.Code, Is.EqualTo(code));
        Assert.That(Assert.Throws<ColumnDefaultPlanningException>(() => renderer.Render(schema))!.Code, Is.EqualTo(code));
    }

    [Test]
    public void ExternalSequence_UsesQualifiedComponentsAndEmitsOnlyTableCommands()
    {
        var sequence = new DatabaseObjectName("next.'\"&", "shared space", "data.store", "catalog]name");
        var expression = ColumnDefaultExpression.NextSequenceValue(sequence);
        var column = new NumericColumn("id", DbType.Decimal, 20, 2, defaultExpression: expression);
        var schema = new Schema([new Table("one", [column]), new Table("two", [column])]);
        var provider = new DefaultPlanningRenderer((dialect, context) =>
        {
            var name = context.Column.DefaultExpression!.Sequence!;
            Assert.That(name, Is.EqualTo(sequence));
            return new("STUB_NEXT " + dialect.SchemaSqlRenderer.RenderObjectName(dialect,
                name.Catalog, name.Database, name.Schema, name.Name));
        });
        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, provider), SchemaCreationOptions.DropIfExists);
        var plan = renderer.Plan(schema);
        var reference = "DEFAULT STUB_NEXT [catalog]]name].[data.store].[shared space].[next.'\"&]";
        Assert.That(plan.Select(command => command.Operation), Is.EqualTo(new[]
            { SchemaSqlOperation.DropTable, SchemaSqlOperation.DropTable, SchemaSqlOperation.CreateTable, SchemaSqlOperation.CreateTable }));
        Assert.That(plan.Skip(2).Select(command => command.CommandText), Has.All.Contains(reference));
        Assert.That(renderer.Render(schema), Does.Contain(reference).And.Not.Contain("CREATE SEQUENCE").And.Not.Contain("DROP SEQUENCE"));

        var unresolved = new SchemaScriptRenderer(new ProviderDialect(source, new DefaultPlanningRenderer((_, _) => null)));
        Assert.That(Assert.Throws<ColumnDefaultPlanningException>(() => unresolved.Plan(schema))!.Code,
            Is.EqualTo(ColumnDefaultPlanningException.Unsupported));
    }

    private static Column ContextColumn(string name, ColumnDefaultExpression expression, bool nullable = true)
        => new VarLengthColumn(name, DbType.String, 64,
            constraints: nullable ? [new NullableConstraint()] : [new NotNullableConstraint()], defaultExpression: expression);

    [TestCase(UuidRepresentation.Native, DbType.Guid, null, null)]
    [TestCase(UuidRepresentation.Binary, DbType.Binary, 16, UuidByteOrder.Network)]
    [TestCase(UuidRepresentation.Binary, DbType.Binary, 16, UuidByteOrder.DotNetMixedEndian)]
    [TestCase(UuidRepresentation.CanonicalText, DbType.String, 36, null)]
    public void UuidDefaults_PreserveRepresentationAndLeaveEvaluationToInsert(UuidRepresentation representation,
        DbType type, int? length, UuidByteOrder? order)
    {
        var expression = ColumnDefaultExpression.NewUuid(representation, order);
        var column = length is { } size ? new VarLengthColumn("id", type, size,
            nativeType: type == DbType.Binary ? new("BINARY") : null, defaultExpression: expression)
            : new Column("id", type, defaultExpression: expression);
        var provider = new DefaultPlanningRenderer((_, context) =>
        {
            Assert.That(context.Column.DefaultExpression, Is.EqualTo(expression));
            return new("STUB_UUID()", UuidSemantics: new(Version: 4));
        });
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, provider),
            nativeTypes: new NativeTypeRegistry().Register<ProviderDialect>("BINARY"))
            .Plan(new([new Table("ids", [column])])).Single().CommandText;
        Assert.That(sql, Does.Contain("DEFAULT STUB_UUID()"));
    }

    private sealed class DefaultPlanningRenderer(Func<IDialect, ColumnDefaultContext, ColumnDefaultPlan?> plan,
        bool ownsDefault = false) : StandardSchemaSqlRenderer, IColumnDefaultSqlRenderer, IColumnDefinitionSqlRenderer
    {
        public List<ColumnDefaultContext> Contexts { get; } = [];

        public ColumnDefaultPlan? PlanColumnDefault(IDialect dialect, ColumnDefaultContext context)
        {
            Contexts.Add(context);
            return plan(dialect, context);
        }

        public ColumnDefinitionPlan? RenderColumnDefinition(IDialect dialect, ColumnDefinitionContext context)
            => ownsDefault && context.DefaultExpression is not null
                ? new($"{context.Type} DEFAULT {context.DefaultExpression}", ColumnDefinitionClauses.Default) : null;

        public override string RenderObjectName(IDialect dialect, string? catalog, string? database, string? schema, string name)
            => string.Join('.', new[] { catalog, database, schema, name }.OfType<string>()
                .Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));
    }

    [TestCase(false, "explicit key", "PROVIDER_NULL_POLICY")]
    [TestCase(false, null, null)]
    [TestCase(true, null, null)]
    public void UniquePlans_ConsumeExplicitTupleOnceAndKeepLegacyTablePlanning(bool useIndex, string? name, string? clause)
    {
        var provider = new UniquePlanningRenderer(useIndex, nullPolicyClause: clause);
        var renderer = new SchemaScriptRenderer(new ProviderDialect(source, provider));
        var table = UniqueTable(name);
        var child = new Table("Child", table.Columns.Values.ToArray(),
            [new ForeignKey("FK_tuple", ["B", "A"], table.Identity, ["B", "A"])]);
        var schema = new Schema([child, table]);
        var text = renderer.Render(schema);
        var commands = renderer.Plan(schema);
        Assert.Multiple(() =>
        {
            Assert.That(provider.Context!.Constraint.Columns.Select(column => column.Name), Is.EqualTo(new[] { "B", "A" }));
            Assert.That(provider.Context.Constraint.Columns[0], Is.SameAs(table.Columns["B"]));
            Assert.That(provider.Context.Constraint.Columns[0].Constraints.Get<NullableConstraint>(), Is.Not.Null);
            Assert.That(provider.Context.SuggestedName, Is.EqualTo(name ?? "UQ_Pair_2"));
            Assert.That(provider.LegacyKeys, Is.EqualTo(new[] { "Legacy" }));
            Assert.That(text.Split("LEGACY_UNIQUE").Length, Is.EqualTo(2));
            Assert.That(commands.Select(command => command.Operation), Is.EqualTo(useIndex
                ? new[] { SchemaSqlOperation.CreateTable, SchemaSqlOperation.CreateTable, SchemaSqlOperation.CreateIndex, SchemaSqlOperation.CreateIndex, SchemaSqlOperation.ForeignKey }
                : new[] { SchemaSqlOperation.CreateTable, SchemaSqlOperation.CreateTable, SchemaSqlOperation.ForeignKey }));
            var constraintSql = name is null ? "UNIQUE ([B], [A])" : "CONSTRAINT [explicit key] UNIQUE PROVIDER_NULL_POLICY ([B], [A])";
            var marker = useIndex ? "CREATE UNIQUE INDEX" : constraintSql;
            Assert.That(text, Does.Contain(marker));
            Assert.That(text.IndexOf(marker, StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("FOREIGN KEY", StringComparison.Ordinal)));
            if (useIndex)
            {
                var indexSql = commands.Where(command => command.Operation == SchemaSqlOperation.CreateIndex)
                    .Select(command => command.CommandText).ToArray();
                Assert.That(indexSql[0], Does.Contain("CREATE UNIQUE INDEX [UQ_Pair_2] ON [Pair] (")
                    .And.Contain($"    [B],{Environment.NewLine}    [A]")
                    .And.Contain(") PROVIDER_KEY_OPTION WHERE [B] <> 'a&b';"));
                Assert.That(indexSql[1], Does.Contain("CREATE UNIQUE INDEX [UQ_Pair_2_aux] ON [Pair] (")
                    .And.Contain(") WHERE [B] IS NULL;"));
                Assert.That(text, Does.Contain(string.Concat(indexSql)));
                Assert.That(text.Split("CREATE UNIQUE INDEX").Length, Is.EqualTo(3));
            }
            else
            {
                Assert.That(text.Split(constraintSql).Length, Is.EqualTo(2));
                Assert.That(commands.Count(command => command.CommandText.Contains(constraintSql, StringComparison.Ordinal)), Is.EqualTo(1));
            }
        });
    }

    [TestCase("unsupported", "requires a provider plan")]
    [TestCase("mismatch", "cannot be enforced")]
    [TestCase("overlap", "overlaps a separate unique index")]
    [TestCase("collision", "ambiguous backing index name")]
    [TestCase("ineligible", "no eligible planned UNIQUE target")]
    [TestCase("reordered", "no eligible planned UNIQUE target")]
    [TestCase("blank clause", "invalid native null-policy clause")]
    public void UniquePlans_RejectUnsupportedOrAmbiguousEnforcement(string failure, string message)
    {
        var table = UniqueTable();
        var targets = failure == "reordered" ? new[] { "A", "B" } : new[] { "B", "A" };
        var child = new Table("Child", table.Columns.Values.ToArray(),
            [new ForeignKey("FK_tuple", ["B", "A"], table.Identity, targets)]);
        Index[] indexes = failure switch
        {
            "overlap" => [new Index("separate", table.Name, [new("A"), new("B")], true)],
            "collision" => [new Index("existing", table.Name, [new("A")])],
            _ => []
        };
        var dialect = failure == "unsupported" ? source : new ProviderDialect(source,
            new UniquePlanningRenderer(failure != "blank clause", failure != "ineligible", failure == "mismatch",
                failure == "collision" ? "existing" : null, failure == "blank clause" ? " " : null));
        var renderer = new SchemaScriptRenderer(dialect);
        var schema = new Schema([child, table], indexes);
        Assert.That(() => renderer.Plan(schema), Throws.TypeOf<NotSupportedException>().With.Message.Contains(message));
        Assert.That(() => renderer.Render(schema), Throws.TypeOf<NotSupportedException>().With.Message.Contains(message));
    }

    [Test]
    public void UniquePlans_PrimaryKeyEquivalentTupleNeedsNoProviderPlan()
    {
        var a = new Column("A", DbType.Int32);
        var b = new Column("B", DbType.Int32);
        var table = new Table("Pair", [a, b],
            [new PrimaryKeyConstraint([a, b]), new UniquenessConstraint([b, a], "Redundant", UniqueNullPolicy.NullsNotDistinct)]);
        var sql = new SchemaScriptRenderer(source).Render(new([table]));
        Assert.That(sql, Does.Contain("PRIMARY KEY").And.Not.Contain("UNIQUE"));
    }

    private static Table UniqueTable(string? name = null)
    {
        var a = new Column("A", DbType.Int32, constraints: [new NotNullableConstraint()]);
        var b = new Column("B", DbType.Int32, constraints: [new NullableConstraint()]);
        return new Table("Pair", [a, b],
            [new UniquenessConstraint([a], "Legacy"), new UniquenessConstraint([b, a], name, UniqueNullPolicy.NullsDistinct)]);
    }

    private sealed class UniquePlanningRenderer(bool useIndex, bool canReference = true, bool mismatch = false, string? backingName = null,
        string? nullPolicyClause = null)
        : StandardSchemaSqlRenderer, IUniqueConstraintSqlRenderer, ITableDefinitionSqlRenderer
    {
        public UniqueConstraintContext? Context { get; private set; }
        public string?[]? LegacyKeys { get; private set; }

        public UniqueConstraintPlan PlanUniqueConstraint(IDialect dialect, UniqueConstraintContext context)
        {
            Context = context;
            var policy = mismatch ? UniqueNullPolicy.Native : context.Constraint.NullPolicy;
            return useIndex
                ? new UniqueIndexPlan(policy, canReference,
                    [
                        new(new(backingName ?? context.SuggestedName), "[B] <> 'a&b'", "PROVIDER_KEY_OPTION"),
                        new(new(context.SuggestedName + "_aux"), "[B] IS NULL")
                    ])
                : new NativeUniqueConstraintPlan(policy, canReference, nullPolicyClause);
        }

        public TableDefinitionPlan RenderTableDefinition(IDialect dialect, TableDefinitionContext table)
        {
            var keys = table.UniqueKeys ?? [];
            if (table.Table.Name == "Pair")
                LegacyKeys = keys.Select(key => key.Name).ToArray();
            return new("CREATE TABLE", keys.Select(_ => "LEGACY_UNIQUE ([A])").ToArray());
        }

        public string RenderCreateIndex(IDialect dialect, IndexDefinitionContext index) => throw new NotSupportedException();
    }

    [Test]
    public void DescriptionOnlyHook_IsIndependentOfPhysicalPlansAndPostCreateComments()
    {
        var renderer = new DescriptionOnlyRenderer();
        var dialect = new ProviderDialect(source, renderer);
        var table = new Table(new DatabaseObjectName("Order", schema: "sales"),
            [new Column("Id", DbType.Int32)], description: "Owner's 雪");
        var scriptRenderer = new SchemaScriptRenderer(dialect);
        var sql = scriptRenderer.Render(new([table]));
        Assert.That(scriptRenderer.SupportsComments, Is.True);
        Assert.That(sql, Does.Contain(") PROVIDER_DESCRIPTION;"));
        Assert.That(renderer.Table, Is.EqualTo(new SchemaSqlObjectName("Order", "sales")));
        Assert.That(renderer.Description, Is.EqualTo("Owner's 雪"));
    }

    private sealed class DescriptionOnlyRenderer : StandardSchemaSqlRenderer, ITableDescriptionSqlRenderer
    {
        public SchemaSqlObjectName? Table { get; private set; }
        public string? Description { get; private set; }
        public string RenderTableDescription(IDialect dialect, SchemaSqlObjectName table, string description)
        {
            Table = table;
            Description = description;
            return " PROVIDER_DESCRIPTION";
        }

        public override string RenderComment(IDialect dialect, SchemaSqlObjectName table, string? column, string description)
            => throw new InvalidOperationException("The description hook must not call post-create comments.");
    }

    [Test]
    public void IdentityOverride_InheritsObjectQuotingAndGeneratedColumnSyntax()
    {
        var dialect = new ProviderDialect(source, new IdentityRenderer(source.SchemaSqlRenderer));
        var table = new Table("Order", [
            new Column("Id", DbType.Int64, generation: new IdentityColumnGeneration(100, 5)),
            new Column("Computed", DbType.Int32, generation: new GeneratedColumnGeneration(new ValueExpression(7)))
        ]);

        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([table], []));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("CREATE TABLE [Order]"));
            Assert.That(sql, Does.Contain("[Id] BIGINT PROVIDER_IDENTITY(100, 5)"));
            Assert.That(sql, Does.Contain("[Computed] AS (7) PERSISTED"));
        });
    }

    [TestCase(GeneratedColumnStorage.Stored, "True")]
    [TestCase(GeneratedColumnStorage.Virtual, "False")]
    public void GeneratedOverride_ReceivesRenderedExpressionAndStorage(GeneratedColumnStorage storage, string expected)
    {
        var dialect = new ProviderDialect(source, new GeneratedRenderer());
        var table = new Table("Order", [new Column("Computed", DbType.String,
            generation: new GeneratedColumnGeneration(new ValueExpression("owner's"), storage))]);

        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([table], []));

        Assert.That(sql, Does.Contain($"[Computed] NVARCHAR PROVIDER_GENERATED('owner''s', {expected})"));
    }

    [Test]
    public void RegexOverride_UsesEscapedInputsAndSharedNullGuard()
    {
        var dialect = new ProviderDialect(source, new RegexRenderer());
        var table = new Table("Customer", [new Column("Name", DbType.String,
            constraints: [new RegexCheckConstraint(new ColumnIdentityExpression("Name"), "owner's", RegexNullBehavior.RejectNull)])]);

        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([table], []));

        Assert.That(sql, Does.Contain("CHECK (([Name] IS NOT NULL AND provider_match([Name], 'owner''s')))"));
    }

    [Test]
    public void ObjectNameOverride_IsUsedForTablesAndForeignKeyTargets()
    {
        var dialect = new ProviderDialect(source, new NameRenderer());
        var table = new Table(new DatabaseObjectName("Child", schema: "sales"), [new Column("ParentId", DbType.Int32)],
            [new ForeignKey("FK_Parent", ["ParentId"], new DatabaseObjectName("Parent", schema: "sales"), ["Id"])]);

        var parent = new Table(new DatabaseObjectName("Parent", schema: "sales"), [new Column("Id", DbType.Int32)]);
        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([parent, table], []));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("CREATE TABLE [provider_sales_Child]"));
            Assert.That(sql, Does.Contain("ALTER TABLE [provider_sales_Child]"));
            Assert.That(sql, Does.Contain("REFERENCES [provider_sales_Parent] ([Id])"));
        });
    }

    [Test]
    public void ForeignKeyPlacementOverride_DoesNotRequireSqliteIdentitySyntax()
    {
        var dialect = new ProviderDialect(source, new InlineForeignKeyRenderer());
        var table = new Table("Child", [new Column("ParentId", DbType.Int32)],
            [new ForeignKey("FK_Parent", ["ParentId"], "Parent", ["Id"])]);

        var parent = new Table("Parent", [new Column("Id", DbType.Int32)]);
        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([parent, table], []));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("CONSTRAINT [FK_Parent] FOREIGN KEY ([ParentId]) REFERENCES [Parent] ([Id])"));
            Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
        });
    }

    [Test]
    public void CapabilitiesAlone_DoNotSelectAnEngineRenderer()
    {
        IDialect dialect = new CapabilityOnlyDialect(source);
        var table = new Table("Order", [new Column("Id", DbType.Int64, generation: new IdentityColumnGeneration(10, 2))]);

        Assert.That(dialect.Capabilities.SupportsIdentityColumns, Is.True);
        Assert.That(
            () => new SchemaScriptRenderer(dialect).Render(new Schema([table], [])),
            Throws.TypeOf<NotSupportedException>());
    }

    // Strategies use only Core contracts; existing engine behavior is reused through explicit delegation.
    [Test]
    public void CommentOverride_ReceivesRawNamesAndDescriptions()
    {
        var dialect = new ProviderDialect(source, new CommentSqlRenderer());
        var table = new Table(new DatabaseObjectName("Order", schema: "sales"),
            [new Column("Id", DbType.Int32, description: "Owner's ID")], description: "Orders");
        var sql = new SchemaScriptRenderer(dialect).Render(new Schema([table], []));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("PROVIDER_COMMENT sales.Order Orders"));
            Assert.That(sql, Does.Contain("PROVIDER_COMMENT sales.Order.Id Owner's ID"));
        });
    }

    [Test]
    public void CommentRendering_DelegatesToFallbackProvider()
    {
        var dialect = new ProviderDialect(source, new IdentityRenderer(source.SchemaSqlRenderer));
        var sql = dialect.SchemaSqlRenderer.RenderComment(dialect, new("Order"), null, "Orders");
        Assert.That(sql, Does.Contain("sys.sp_addextendedproperty"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ColumnPlan_ReceivesKeyContextAndConsumesClauses(bool tableKey)
    {
        var id = new Column("Id", DbType.Int32,
            constraints: tableKey ? [new NotNullableConstraint()] : [new PrimaryKeyConstraint("PK_Order"), new NotNullableConstraint()],
            generation: new IdentityColumnGeneration());
        var table = new Table(new DatabaseObjectName("Order", schema: "sales"), [id],
            tableKey ? [new PrimaryKeyConstraint(id, "PK_Order")] : []);
        var renderer = new ColumnPlanRenderer(new("INTEGER PROVIDER_KEY", ColumnDefinitionClauses.Generation
            | ColumnDefinitionClauses.PrimaryKey | ColumnDefinitionClauses.Nullability));
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([table]));
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("[Id] INTEGER PROVIDER_KEY"));
            Assert.That(sql, Does.Not.Contain("PRIMARY KEY"));
            Assert.That(sql, Does.Not.Contain("NOT NULL"));
            Assert.That(renderer.Context!.Table, Is.EqualTo(new SchemaSqlObjectName("Order", "sales")));
            Assert.That(renderer.Context.Column, Is.EqualTo("Id"));
            Assert.That(renderer.Context.Type, Is.EqualTo("INTEGER"));
            Assert.That(renderer.Context.PrimaryKeyColumns, Is.EqualTo(new[] { "Id" }));
            Assert.That(renderer.Context.PrimaryKeyName, Is.EqualTo("PK_Order"));
            Assert.That(renderer.Context.TablePrimaryKey, Is.EqualTo(tableKey));
            Assert.That(renderer.Context.NotNullable, Is.True);
            Assert.That(renderer.Context.Generation, Is.EqualTo(new ColumnGenerationContext(true)));
        });
    }

    [Test]
    public void DecliningColumnPlan_PreservesLegacyIdentityAndGeneratedHooks()
    {
        var renderer = new ColumnPlanRenderer(null);
        var table = new Table("Order", [
            new Column("Id", DbType.Int32, generation: new IdentityColumnGeneration(10, 2)),
            new Column("Copy", DbType.Int32, generation: new GeneratedColumnGeneration(new ValueExpression(7)))
        ]);
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([table]));
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("INTEGER LEGACY_IDENTITY(10, 2)"));
            Assert.That(sql, Does.Contain("INTEGER LEGACY_GENERATED(7)"));
            Assert.That(renderer.Context!.Generation!.Expression, Is.EqualTo("7"));
            Assert.That(renderer.Context.Generation.Stored, Is.True);
        });
    }

    [TestCase(ColumnDefinitionClauses.None, true)]
    [TestCase(ColumnDefinitionClauses.Unique | ColumnDefinitionClauses.Default | ColumnDefinitionClauses.Checks | ColumnDefinitionClauses.Comment, false)]
    public void ColumnPlan_PreservesOnlyUnclaimedModifiers(ColumnDefinitionClauses clauses, bool shared)
    {
        var column = new Column("Id", DbType.Int32, defaultValue: 3, description: "id",
            constraints: [new UniquenessConstraint(), new CheckConstraint(new ValueExpression(1), "=", new ValueExpression(1))]);
        var renderer = new ColumnPlanRenderer(new("INTEGER PROVIDER", clauses));
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([new Table("Order", [column])]));
        Assert.Multiple(() =>
        {
            Assert.That(sql.Contains(" UNIQUE", StringComparison.Ordinal), Is.EqualTo(shared));
            Assert.That(sql.Contains(" DEFAULT 3", StringComparison.Ordinal), Is.EqualTo(shared));
            Assert.That(sql.Contains(" CHECK (1 = 1)", StringComparison.Ordinal), Is.EqualTo(shared));
            Assert.That(sql.Contains(" INLINE_COMMENT", StringComparison.Ordinal), Is.EqualTo(shared));
            Assert.That(sql.Contains("SEPARATE_COMMENT", StringComparison.Ordinal), Is.EqualTo(shared));
            Assert.That(renderer.Context!.DefaultExpression, Is.EqualTo("3"));
            Assert.That(renderer.Context.Checks, Is.EqualTo(new[] { "1 = 1" }));
            Assert.That(renderer.Context.Description, Is.EqualTo("id"));
        });
    }

    [Test]
    public void ColumnPlan_CannotConsumeCompositePrimaryKey()
    {
        var id = new Column("Id", DbType.Int32);
        var other = new Column("Other", DbType.Int32);
        var table = new Table("Order", [id, other], [new PrimaryKeyConstraint([id, other])]);
        var renderer = new ColumnPlanRenderer(new("INTEGER", ColumnDefinitionClauses.PrimaryKey));
        Assert.That(() => new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([table])),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("single-column primary key"));
    }

    [TestCase("", ColumnDefinitionClauses.Generation)]
    [TestCase("INTEGER", ColumnDefinitionClauses.None)]
    public void ColumnPlan_RejectsIncompleteSpecification(string specification, ColumnDefinitionClauses clauses)
    {
        var table = new Table("Order", [new Column("Id", DbType.Int32, generation: new IdentityColumnGeneration())]);
        var renderer = new ColumnPlanRenderer(new(specification, clauses));
        Assert.That(() => new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([table])),
            Throws.TypeOf<NotSupportedException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ColumnPlan_CoordinatesSequencePrerequisites(bool consumeGeneration)
    {
        var renderer = new SequencePlanRenderer(consumeGeneration);
        var table = new Table("Order", [new Column("Id", DbType.Int32, generation: new IdentityColumnGeneration())]);
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new Schema([table]));
        Assert.Multiple(() =>
        {
            Assert.That(sql.Contains("CREATE_SEQUENCE", StringComparison.Ordinal), Is.EqualTo(!consumeGeneration));
            Assert.That(sql.Contains("SEQUENCE_VALUE", StringComparison.Ordinal), Is.EqualTo(!consumeGeneration));
            Assert.That(sql.Contains("PLANNED_GENERATION", StringComparison.Ordinal), Is.EqualTo(consumeGeneration));
        });
    }

    private sealed class SequencePlanRenderer(bool consumeGeneration) : StandardSchemaSqlRenderer, IColumnDefinitionSqlRenderer, ISequenceIdentitySqlRenderer
    {
        public ColumnDefinitionPlan? RenderColumnDefinition(IDialect dialect, ColumnDefinitionContext column)
            => consumeGeneration ? new("INTEGER PLANNED_GENERATION", ColumnDefinitionClauses.Generation) : null;
        public string RenderIdentityPrerequisite(IDialect dialect, IdentityColumnContext column, long seed, long increment, bool dropExisting)
            => "CREATE_SEQUENCE;";
        public string RenderIdentityColumn(IDialect dialect, IdentityColumnContext column, string type) => $"{type} SEQUENCE_VALUE";
    }

    private sealed class ColumnPlanRenderer(ColumnDefinitionPlan? plan) : StandardSchemaSqlRenderer, IColumnDefinitionSqlRenderer
    {
        public override bool SupportsCommentRendering => true;
        public override string RenderComment(IDialect dialect, SchemaSqlObjectName table, string? column, string description) => "SEPARATE_COMMENT";
        public ColumnDefinitionContext? Context { get; private set; }
        public ColumnDefinitionPlan? RenderColumnDefinition(IDialect dialect, ColumnDefinitionContext column)
        {
            Context = column;
            return plan;
        }

        public override string RenderIdentity(IDialect dialect, string type, long seed, long increment)
            => $"{type} LEGACY_IDENTITY({seed}, {increment})";
        public override string RenderGenerated(IDialect dialect, string type, string expression, bool stored)
            => $"{type} LEGACY_GENERATED({expression})";
        public override string RenderColumnComment(IDialect dialect, string description) => " INLINE_COMMENT";
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CombinedColumnAndTablePlans_PreserveKeyContextWithoutDuplicateEmission(bool tableKey)
    {
        var id = new Column("Id", DbType.Int32, constraints: tableKey ? [] : [new PrimaryKeyConstraint("PK_Order")]);
        var table = new Table("Order", [id], tableKey ? [new PrimaryKeyConstraint(id, "PK_Order")] : []);
        var renderer = new CombinedPlanRenderer(false);
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new([table]));
        Assert.That(renderer.ColumnContext!.PrimaryKeyColumns, Is.EqualTo(new[] { "Id" }));
        Assert.That(sql, Does.Contain("PROVIDER_PRIMARY_KEY"));
        Assert.That(sql, Does.Not.Contain("PRIMARY KEY"));
    }

    [Test]
    public void CombinedColumnAndTablePlans_RejectConflictingKeyOwnership()
    {
        var table = new Table("Order", [new Column("Id", DbType.Int32, constraints: [new PrimaryKeyConstraint()])]);
        Assert.Throws<NotSupportedException>(() => new SchemaScriptRenderer(new ProviderDialect(source,
            new CombinedPlanRenderer(true))).Render(new([table])));
    }

    [Test]
    public void CombinedColumnAndTablePlans_PreserveColumnUniquenessContext()
    {
        var table = new Table("Order", [new Column("Code", DbType.Int32, constraints: [new UniquenessConstraint()])]);
        var renderer = new CombinedPlanRenderer(false);
        var sql = new SchemaScriptRenderer(new ProviderDialect(source, renderer)).Render(new([table]));
        Assert.That(renderer.ColumnContext!.Unique, Is.True);
        Assert.That(sql, Does.Not.Contain(" UNIQUE"));
    }

    private sealed class CombinedPlanRenderer(bool claimKey) : StandardSchemaSqlRenderer, IColumnDefinitionSqlRenderer, ITableDefinitionSqlRenderer
    {
        public ColumnDefinitionContext? ColumnContext { get; private set; }
        public ColumnDefinitionPlan? RenderColumnDefinition(IDialect dialect, ColumnDefinitionContext column)
        {
            ColumnContext = column;
            return new(column.Type, claimKey ? ColumnDefinitionClauses.PrimaryKey : ColumnDefinitionClauses.None);
        }

        public TableDefinitionPlan RenderTableDefinition(IDialect dialect, TableDefinitionContext table)
            => new("CREATE TABLE", ["PROVIDER_PRIMARY_KEY"]);
        public string RenderCreateIndex(IDialect dialect, IndexDefinitionContext index) => throw new NotSupportedException();
    }

    private sealed class CommentSqlRenderer : StandardSchemaSqlRenderer
    {
        public override bool SupportsCommentRendering => true;
        public override string RenderComment(IDialect dialect, SchemaSqlObjectName table, string? column, string description)
            => $"PROVIDER_COMMENT {table.Schema}.{table.Name}{(column is null ? string.Empty : "." + column)} {description}";
    }

    private sealed class IdentityRenderer(ISchemaSqlRenderer fallback) : StandardSchemaSqlRenderer(fallback)
    {
        public override string RenderIdentity(IDialect dialect, string type, long seed, long increment)
            => $"{type} PROVIDER_IDENTITY({seed}, {increment})";
    }

    private sealed class GeneratedRenderer : StandardSchemaSqlRenderer
    {
        public override string RenderGenerated(IDialect dialect, string type, string expression, bool stored)
            => $"{type} PROVIDER_GENERATED({expression}, {stored})";
    }

    private sealed class RegexRenderer : StandardSchemaSqlRenderer
    {
        public override string RenderRegex(IDialect dialect, string expression, string pattern)
            => $"provider_match({expression}, {pattern})";
    }

    private sealed class NameRenderer : StandardSchemaSqlRenderer
    {
        public override string RenderObjectName(IDialect dialect, string? catalog, string? database, string? schema, string name)
            => dialect.Renderer.Render($"provider_{schema}_{name}", "identity");
    }

    private sealed class InlineForeignKeyRenderer : StandardSchemaSqlRenderer
    {
        public override bool RenderForeignKeysInline(IDialect dialect) => true;
    }

    private sealed class ProviderDialect(IDialect source, ISchemaSqlRenderer renderer)
        : BaseDialect(source.Language, source.Aliases, source.Renderer, source.Casters, source.DbTypeMapper, source.SqlFunctionMapper)
    {
        public override DialectCapabilities Capabilities => source.Capabilities;
        public override ISchemaSqlRenderer SchemaSqlRenderer => renderer;
    }

    private sealed class CapabilityOnlyDialect(IDialect source) : IDialect
    {
        public IRenderer Renderer => source.Renderer;
        public ICaster[] Casters => source.Casters;
        public string[] Aliases => source.Aliases;
        public ILanguage Language => source.Language;
        public IDbTypeMapper DbTypeMapper => source.DbTypeMapper;
        public ISqlFunctionMapper SqlFunctionMapper => source.SqlFunctionMapper;
        public DialectCapabilities Capabilities => source.Capabilities;
    }
}
