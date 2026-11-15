using DubUrl.Discovery;

namespace DubUrl.Schema;

/// <summary>Optional provider access to explicit hierarchy declarations stored with catalog metadata.</summary>
public interface IHierarchyMetadataReader
{
    /// <summary>Returns a declaration recorded in metadata, or null when no declaration was recorded.</summary>
    /// <remarks>A matching column name or self-referencing foreign key alone is insufficient evidence.</remarks>
    Hierarchy? GetHierarchy(MetadataTable table);
}