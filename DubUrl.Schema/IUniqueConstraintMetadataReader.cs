using DubUrl.Discovery;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema;

/// <summary>Optional metadata supplied by a provider alongside the existing discovery reader.</summary>
public interface IUniqueConstraintMetadataReader
{
    /// <summary>Returns known tuple semantics and any provider-confirmed backing index identities.</summary>
    UniqueConstraintMetadata? GetUniqueConstraintMetadata(MetadataTable table, MetadataKey key);
}

public sealed record UniqueConstraintMetadata(UniqueNullPolicy NullPolicy, IReadOnlyList<MetadataName>? BackingIndexes = null);
