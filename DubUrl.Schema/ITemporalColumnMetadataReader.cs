using DubUrl.Discovery;

namespace DubUrl.Schema;

/// <summary>Supplies portable fractional-second precision for native temporal column declarations.</summary>
public interface ITemporalColumnMetadataReader : ISchemaMetadataReader
{
    /// <summary>Returns a precision when the logical type and this facet fully represent the native declaration.</summary>
    int? GetFractionalSecondsPrecision(MetadataColumn column);
}
