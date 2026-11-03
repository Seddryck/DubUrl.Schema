using System.Data;
using DubUrl.Provider.MySql.Sql;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using NUnit.Framework;

namespace DubUrl.Schema.Testing;

public class TableDefinitionTests
{
    private static IDialect Dialect(bool singleStore = true)
    {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<SingleStoreDialect>(["singlestore"]);
        builder.AddDialect<MySqlDialect>(["mysql"]);
        var registry = builder.Build();
        return singleStore ? registry.Get<SingleStoreDialect>() : registry.Get<MySqlDialect>();
    }

    [Test]
    public void Descriptions_AreRenderedOnceInCreateTable()
    {
        var table = new Table(new DatabaseObjectName("orders", database: "shop"),
            [new Column("id", DbType.Int32, description: "Column's 雪")], description: "Owner's 雪\r\nTable");
        var sql = new SchemaScriptRenderer(Dialect()).Render(new([table]));
        Assert.That(sql, Does.Contain(") COMMENT = 'Owner''s 雪"));
        Assert.That(sql, Does.Contain("Column''s 雪"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
        Assert.That(sql.Split("Owner''s").Length, Is.EqualTo(2));
    }

    [Test]
    public void ProviderPlan_ConsumesInlineKeysAndIndexesExactlyOnce()
    {
        var table = new Table(new DatabaseObjectName("items"),
            [new Column("id", DbType.Int32, constraints: [new PrimaryKeyConstraint()]),
             new Column("code", DbType.Int32, constraints: [new UniquenessConstraint()])], null, null,
            new(SchemaTableStorage.Rowstore, ["id"]));
        // An incompatible inline unique key must reach the provider instead of being dropped.
        Assert.Throws<NotSupportedException>(() => new SchemaScriptRenderer(Dialect()).Render(new([table])));
        table = new Table(table.Identity, table.Columns.Values.ToArray(), null, null,
            new(SchemaTableStorage.Rowstore, ["code"]));
        Assert.Throws<NotSupportedException>(() => new SchemaScriptRenderer(Dialect()).Render(new([table])));
    }

    [Test]
    public void Rowstore_UsesShardCompatibleUniqueAndOrderedIndexes()
    {
        var id = new Column("id", DbType.Int32, constraints: [new PrimaryKeyConstraint()]);
        var code = new Column("code", DbType.Int32);
        var table = new Table(new("items"), [id, code], [new UniquenessConstraint([id, code])], null,
            new(SchemaTableStorage.Rowstore, ["id"]));
        var index = new Index(new("ordered"), table.Identity,
            [new("id", IndexSortDirection.Descending), new("code", IndexSortDirection.Ascending)], true, SchemaIndexStructure.Ordered);
        var sql = new SchemaScriptRenderer(Dialect()).Render(new([table], [index]));
        Assert.That(sql, Does.Contain("CREATE ROWSTORE TABLE"));
        Assert.That(sql, Does.Contain("PRIMARY KEY (`id`)"));
        Assert.That(sql, Does.Contain("SHARD KEY (`id`)"));
        Assert.That(sql, Does.Contain("UNIQUE KEY `ordered` (`id` DESC, `code` ASC) USING BTREE"));
        Assert.That(sql, Does.Not.Contain("CREATE UNIQUE INDEX"));
        Assert.That(sql.Split("PRIMARY KEY").Length, Is.EqualTo(2));
    }

    [Test]
    public void UnsupportedOptionsAndHashDirection_AreRejected()
    {
        var table = new Table(new("items"), [new Column("id", DbType.Int32)], null, null, new());
        Assert.Throws<NotSupportedException>(() => new SchemaScriptRenderer(Dialect(false)).Render(new([table])));
        var plain = new Table("items", table.Columns.Values.ToArray());
        var index = new Index("idx", "items", [new("id", IndexSortDirection.Descending)]);
        Assert.Throws<NotSupportedException>(() => new SchemaScriptRenderer(Dialect()).Render(new([plain], [index])));
    }

    [Test]
    public void Options_AreValidatedAndCopiedThroughBuilders()
    {
        string[] shard = ["id"];
        var table = new TableBuilder().WithName("items").WithColumns(columns => columns
            .Add(column => column.WithName("id").WithType(DbType.Int32)))
            .WithPhysicalOptions(new(SchemaTableStorage.Rowstore, shard)).Build();
        shard[0] = "missing";
        Assert.That(table.PhysicalOptions!.ShardKey, Is.EqualTo(new[] { "id" }));
        Assert.Throws<ArgumentException>(() => new Table(table.Identity, table.Columns.Values.ToArray(), null, null,
            new(ShardKey: ["missing"])));
        Assert.Throws<ArgumentException>(() => new Table(table.Identity, table.Columns.Values.ToArray(), null, null,
            new(SortKey: [new("id"), new("id")])));
        var index = new IndexBuilder().WithName("idx").OnTable("items").WithColumns(columns => columns
            .Add(column => column.WithName("id"))).WithStructure(SchemaIndexStructure.Ordered).Build();
        Assert.That(index.Structure, Is.EqualTo(SchemaIndexStructure.Ordered));
    }
}
