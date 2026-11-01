using DubUrl.Provider.SqlServer.Sql;
using System.Data;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Casters;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Querying.Dialects.Renderers;
using DubUrl.Querying.TypeMapping;
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
