using System.Data.Common;
using SingleStoreConnector;
using DubUrl.Discovery;
using DubUrl.Schema;
using DubUrl.Schema.QA;

namespace DubUrl.Provider.SingleStore.QA;

public sealed partial class SingleStoreSchemaTests
{
    protected override IReadOnlyDictionary<string,FeatureExpectation> FeatureExpectations { get; } =
        new Dictionary<string,FeatureExpectation>
        {
            ["ReplaceTable"] = FeatureExpectation.Supported,
            ["QuotedIdentifiers"] = FeatureExpectation.Supported,
            ["TableComments"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore server accepts ALTER TABLE COMMENT but does not persist table comments; column comments do persist."),
            ["ColumnComments"] = FeatureExpectation.Supported,
            ["EmptyComments"] = FeatureExpectation.Supported,
            ["CommentsSameBatch"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore server accepts ALTER TABLE COMMENT but does not persist table comments; column comments do persist."),
            ["QualifiedComments"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore server accepts ALTER TABLE COMMENT but does not persist table comments; column comments do persist."),
            ["UnqualifiedComments"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore server accepts ALTER TABLE COMMENT but does not persist table comments; column comments do persist."),
            ["ColumnTypes"] = FeatureExpectation.Supported,
            ["ColumnFacets"] = FeatureExpectation.Supported,
            ["Nullability"] = FeatureExpectation.Supported,
            ["LiteralDefaults"] = FeatureExpectation.Supported,
            ["IdentityDefault"] = FeatureExpectation.Supported,
            ["IdentitySeedIncrement"] = new(ExpectedOutcome.RendererRejects,"The MySQL renderer rejects per-column seed/increment settings.","seed"),
            ["GeneratedStored"] = new(ExpectedOutcome.DatabaseRejects,"The MySQL generated-column form is rejected by SingleStore.","1064"),
            ["GeneratedVirtual"] = new(ExpectedOutcome.DatabaseRejects,"The MySQL generated-column form is rejected by SingleStore.","1064"),
            ["SinglePrimaryKey"] = FeatureExpectation.Supported,
            ["CompositePrimaryKey"] = FeatureExpectation.Supported,
            ["SingleUnique"] = new(ExpectedOutcome.DatabaseRejects,"Portable UNIQUE declarations omit SingleStore shard-key requirements.","1896"),
            ["CompositeUnique"] = new(ExpectedOutcome.DatabaseRejects,"Portable UNIQUE declarations omit SingleStore shard-key requirements.","1896"),
            ["ForeignKey"] = new(ExpectedOutcome.DatabaseRejects,"SingleStore does not support enforced foreign key constraints.","2752"),
            ["CompositeForeignKey"] = new(ExpectedOutcome.DatabaseRejects,"SingleStore does not support enforced foreign key constraints.","2752"),
            ["DependencyCreate"] = new(ExpectedOutcome.DatabaseRejects,"SingleStore does not support enforced foreign key constraints.","2752"),
            ["DependencyDrop"] = new(ExpectedOutcome.DatabaseRejects,"SingleStore does not support enforced foreign key constraints.","2752"),
            ["ComparisonCheck"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore engine accepts CHECK clauses without enforcing them."),
            ["MembershipCheck"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore engine accepts CHECK clauses without enforcing them."),
            ["RegexCheck"] = new(ExpectedOutcome.NotEnforced,"The pinned SingleStore engine accepts CHECK clauses without enforcing them."),
            ["SingleIndex"] = FeatureExpectation.Supported,
            ["CompositeIndex"] = FeatureExpectation.Supported,
            ["UniqueIndex"] = new(ExpectedOutcome.DatabaseRejects,"Portable UNIQUE declarations omit SingleStore shard-key requirements.","1896"),
            ["IndexDirection"] = new(ExpectedOutcome.NotEnforced,"SingleStore does not retain descending key order."),
            ["ProjectionView"] = FeatureExpectation.Supported,
            ["DiscoveryTablesColumns"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryFacets"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryPrimaryKey"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryUnique"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryForeignKey"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryDefaults"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryIdentity"] = new(ExpectedOutcome.NoDiscovery,"No schema discovery capability is registered."),
            ["DiscoveryChecks"] = new(ExpectedOutcome.NoDiscovery,"No schema discovery capability is registered."),
            ["DiscoveryGeneration"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryComments"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryIndexes"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryViews"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryTableFilter"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoverySchemaFilter"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryClosure"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryPartial"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryStrict"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiscoveryRoundTrip"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["NativeVarcharKey"] = new(ExpectedOutcome.RendererRejects,"The provider does not register these explicit native type names.","native type"),
            ["NativeCharKey"] = new(ExpectedOutcome.RendererRejects,"The provider does not register these explicit native type names.","native type"),
            ["ExpressionIndexDiagnostic"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["FilteredIndexDiagnostic"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["AggregateViewDiagnostic"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["MissingReferenceDiagnostic"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["DiagnosticDetails"] = new(ExpectedOutcome.NoDiscovery,"The published provider does not register SchemaDiscoveryCapability; SDI001 is required."),
            ["PublicDeployment"] = FeatureExpectation.Supported,
            ["FluentDeployment"] = FeatureExpectation.Supported,
            ["CleanupAfterFailure"] = FeatureExpectation.Supported,
        };
    protected override void ExecuteScript(DbConnection connection,string script) => QaSql.Execute(connection,script);
    protected override string DatabaseErrorCode(DbException exception) => ((SingleStoreException)exception).Number.ToString();
    protected override string CurrentSchema(DbConnection connection) => connection.Database;
    protected override DatabaseObjectName QualifiedName(DbConnection connection,string name) => new(name,database:CurrentSchema(connection));
    protected override ISchemaMetadataReader? CreateMetadataReader() => null;
    protected override int? ExpectedDiscoveredStringLength => null;
    protected override GeneratedColumnStorage DiscoveryGenerationStorage => GeneratedColumnStorage.Stored;
    protected override PublicDatabase CreatePublicDatabase()
    {
        DbProviderFactories.RegisterFactory("SingleStoreConnector",SingleStoreConnectorFactory.Instance);
        return QaSql.Public("singlestore",CreateConnection());
    }
    protected override string ExpressionIndexSql(string index,string table,string column) => $"CREATE INDEX {index} ON {table} ((UPPER({column})))";
    protected override string FilteredIndexSql(string index,string table,string column) => $"CREATE INDEX {index} ON {table} ({column}) WHERE {column}>0";
    protected override IReadOnlyList<ColumnFact> ReadColumns(DbConnection connection,DatabaseObjectName table)
        => QaSql.Rows(connection,"SELECT COLUMN_NAME,DATA_TYPE,CHARACTER_MAXIMUM_LENGTH,NUMERIC_PRECISION,NUMERIC_SCALE,IS_NULLABLE FROM information_schema.columns WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name ORDER BY ORDINAL_POSITION",("name",table.Name))
            .Select(r=>new ColumnFact(QaSql.Text(r[0]),QaSql.Text(r[1]),QaSql.Int(r[2]),QaSql.Int(r[3]),QaSql.Int(r[4]),QaSql.Text(r[5])=="YES")).ToArray();
    protected override IReadOnlyList<IndexFact> ReadIndexes(DbConnection connection,DatabaseObjectName table)
        => QaSql.Rows(connection,"SELECT INDEX_NAME,COLUMN_NAME,SEQ_IN_INDEX,NON_UNIQUE,COLLATION FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name ORDER BY INDEX_NAME,SEQ_IN_INDEX",("name",table.Name))
            .Select(r=>new IndexFact(QaSql.Text(r[0]),QaSql.Text(r[1]),Convert.ToInt32(r[2]),Convert.ToInt32(r[3])==0,QaSql.Text(r[4])=="D")).ToArray();
    protected override CommentFact ReadComments(DbConnection connection,DatabaseObjectName table)
    {
        var description=QaSql.Text(QaSql.Rows(connection,"SELECT TABLE_COMMENT FROM information_schema.tables WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name",("name",table.Name)).Single()[0]);
        var columns=QaSql.Rows(connection,"SELECT COLUMN_NAME,COLUMN_COMMENT FROM information_schema.columns WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name",("name",table.Name));
        return new(string.IsNullOrEmpty(description) ? null : description,columns.ToDictionary(r=>QaSql.Text(r[0]),r=>(string?) (string.IsNullOrEmpty(QaSql.Text(r[1])) ? null : QaSql.Text(r[1]))));
    }
    protected override bool ObjectExists(DbConnection connection,DatabaseObjectName name,bool view)
        => Convert.ToInt64(QaSql.Rows(connection,"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name AND table_type=@kind",("name",name.Name),("kind",view ? "VIEW" : "BASE TABLE")).Single()[0])!=0;
}
