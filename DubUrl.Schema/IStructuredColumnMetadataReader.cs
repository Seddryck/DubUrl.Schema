using System.Data.Common;
using DubUrl.Discovery;

namespace DubUrl.Schema;

/// <summary>Optional decoding of structured types alongside the provider's metadata reader.</summary>
public interface IStructuredColumnMetadataReader : ISchemaMetadataReader
{
    /// <summary>
    /// Returns the catalog snapshot and structured evidence together. Omit entries for legacy
    /// scalar columns. For recognized structured columns with unknown shape or storage, include
    /// an entry with a null type and incomplete coverage. A known structured descriptor supersedes
    /// catalog scalar and native type hints.
    /// </summary>
    Task<StructuredSchemaMetadata> ReadWithStructuredTypesAsync(DbConnection connection,
        DiscoveryOptions options, CancellationToken cancellationToken);
}

public sealed record StructuredSchemaMetadata(SchemaMetadata Metadata,
    IReadOnlyDictionary<StructuredColumnIdentity, StructuredColumnMetadata> StructuredColumns);

public sealed record StructuredColumnIdentity(MetadataName Table, string Column);

/// <summary>Catalog evidence for a shape and storage. Coverage describes metadata, not database enforcement.</summary>
public sealed record StructuredColumnMetadata(StructuredColumnType? Type, MetadataCoverage Coverage);
