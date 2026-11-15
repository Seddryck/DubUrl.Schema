using System.Data;
using DubUrl.Schema.Constraints;
using DubUrl.Schema.Documents.V1;
using NUnit.Framework;

namespace DubUrl.Schema.Documents.Testing;

public class HierarchyDocumentTests
{
    [TestCase(SchemaDocumentFormat.Json)]
    [TestCase(SchemaDocumentFormat.Yaml)]
    public void RoundTripPreservesTheLogicalDeclaration(SchemaDocumentFormat format)
    {
        var key = new Column("id", DbType.Int32);
        var hierarchy = new Hierarchy("id", requireParentExists: false, parentColumnName: "manager_id");
        var table = new Table(new DatabaseObjectName("nodes"), [key], [new PrimaryKeyConstraint(key)],
            null, null, null, hierarchy);
        using var stream = new MemoryStream();

        new SchemaDocumentWriter().Write(stream, new SchemaDocumentExporter().Map(new([table])), format);
        stream.Position = 0;
        var result = new SchemaDocumentMapper().Map(new SchemaDocumentReader().Read(stream, format));

        Assert.That(result.Tables["nodes"].Hierarchy, Is.EqualTo(hierarchy));
        Assert.That(result.Tables["nodes"].Columns.Keys, Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public void ValidatorRequiresTheSingleColumnPrimaryKey()
    {
        var document = new SchemaDocument
        {
            Version = 1,
            Tables = [new()
            {
                Identity = new() { Name = "nodes" },
                Columns = [new() { Name = "id", Type = "int32" }, new() { Name = "label", Type = "string" }],
                PrimaryKey = new() { Columns = ["id"] },
                Hierarchy = new() { NodeKey = "label" }
            }]
        };

        var diagnostics = new SchemaDocumentValidator().Validate(document);

        Assert.That(diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)),
            Does.Contain((SchemaDocumentDiagnosticCodes.InvalidHierarchy, "tables[0].hierarchy.nodeKey")));
    }
}