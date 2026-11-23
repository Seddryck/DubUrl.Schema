using System.Collections.Immutable;
using System.Data.Common;
using DubUrl.Discovery;

namespace DubUrl.Schema;

/// <summary>Optional provider hook for discovering tags separately from annotations.</summary>
public interface ISchemaTagMetadataReader : ISchemaMetadataReader
{
    /// <summary>Reads tags for the catalog snapshot, using the same connection and discovery scope.</summary>
    /// <remarks>Report incomplete coverage for each affected scope and return any tags recovered. Report
    /// Unsupported for scopes the provider does not implement. Use the identities from the supplied snapshot.</remarks>
    Task<SchemaTagMetadata> ReadTagsAsync(DbConnection connection, DiscoveryOptions options, SchemaMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>Discovered tags and coverage for the table and column scopes.</summary>
public sealed record SchemaTagMetadata
{
    public MetadataCoverage TableCoverage { get; init; } = MetadataCoverage.Unsupported;
    public MetadataCoverage ColumnCoverage { get; init; } = MetadataCoverage.Unsupported;
    public IReadOnlyDictionary<MetadataName, IReadOnlyDictionary<string, string>> Tables { get; init; }
        = ImmutableDictionary<MetadataName, IReadOnlyDictionary<string, string>>.Empty;
    public IReadOnlyDictionary<(MetadataName Table, string Column), IReadOnlyDictionary<string, string>> Columns { get; init; }
        = ImmutableDictionary<(MetadataName Table, string Column), IReadOnlyDictionary<string, string>>.Empty;
}
