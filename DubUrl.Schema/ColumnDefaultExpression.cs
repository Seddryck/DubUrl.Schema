using System.Data;

namespace DubUrl.Schema;

public enum ColumnDefaultKind
{
    CurrentTimestamp,
    CurrentDate,
    CurrentTime,
    CurrentUtcTimestamp,
    CurrentUser,
    SessionUser,
    CurrentSchema,
    CurrentDatabase,
    NewUuid
}

public enum UuidRepresentation
{
    Native,
    Binary,
    CanonicalText
}

public enum UuidByteOrder
{
    Network,
    DotNetMixedEndian
}

/// <summary>An allowlisted database expression evaluated when an INSERT uses the column default.</summary>
public sealed record ColumnDefaultExpression
{
    public ColumnDefaultKind Kind { get; }
    public int? FractionalSecondsPrecision { get; }
    public UuidRepresentation? Representation { get; }
    public UuidByteOrder? ByteOrder { get; }

    private ColumnDefaultExpression(ColumnDefaultKind kind, int? fractionalSecondsPrecision = null,
        UuidRepresentation? representation = null, UuidByteOrder? byteOrder = null)
    {
        if (fractionalSecondsPrecision is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(fractionalSecondsPrecision), "Fractional seconds precision must be between 0 and 9.");
        Kind = kind;
        FractionalSecondsPrecision = fractionalSecondsPrecision;
        Representation = representation;
        ByteOrder = byteOrder;
    }

    /// <summary>Session-local timestamp, with the session offset when the destination carries an offset.</summary>
    public static ColumnDefaultExpression CurrentTimestamp(int? fractionalSecondsPrecision = null)
        => new(ColumnDefaultKind.CurrentTimestamp, fractionalSecondsPrecision);

    public static ColumnDefaultExpression CurrentDate { get; } = new(ColumnDefaultKind.CurrentDate);

    /// <summary>Session-local time of day.</summary>
    public static ColumnDefaultExpression CurrentTime(int? fractionalSecondsPrecision = null)
        => new(ColumnDefaultKind.CurrentTime, fractionalSecondsPrecision);

    /// <summary>UTC timestamp independent of session and server time zones, with offset zero when stored with an offset.</summary>
    public static ColumnDefaultExpression CurrentUtcTimestamp(int? fractionalSecondsPrecision = null)
        => new(ColumnDefaultKind.CurrentUtcTimestamp, fractionalSecondsPrecision);

    /// <summary>Gets the effective database authorization identity expression.</summary>
    /// <value>The current-user default.</value>
    public static ColumnDefaultExpression CurrentUser { get; } = new(ColumnDefaultKind.CurrentUser);
    /// <summary>Gets the session authorization identity expression, including changes permitted by the database.</summary>
    /// <value>The session-user default.</value>
    public static ColumnDefaultExpression SessionUser { get; } = new(ColumnDefaultKind.SessionUser);
    /// <summary>Gets the expression for the schema selected by the inserting session.</summary>
    /// <value>The current-schema default.</value>
    public static ColumnDefaultExpression CurrentSchema { get; } = new(ColumnDefaultKind.CurrentSchema);
    /// <summary>Gets the expression for the database selected by the inserting session.</summary>
    /// <value>The current-database default.</value>
    public static ColumnDefaultExpression CurrentDatabase { get; } = new(ColumnDefaultKind.CurrentDatabase);

    public static ColumnDefaultExpression NewUuid(UuidRepresentation representation, UuidByteOrder? byteOrder = null)
    {
        if (!Enum.IsDefined(representation))
            throw new ArgumentOutOfRangeException(nameof(representation));
        if (representation == UuidRepresentation.Binary ? byteOrder is null || !Enum.IsDefined(byteOrder.Value) : byteOrder is not null)
            throw new ArgumentException("An explicit byte order is required for binary UUIDs and applies only to binary UUIDs.", nameof(byteOrder));
        return new(ColumnDefaultKind.NewUuid, representation: representation, byteOrder: byteOrder);
    }

    internal bool IsContext => Kind is ColumnDefaultKind.CurrentUser or ColumnDefaultKind.SessionUser
        or ColumnDefaultKind.CurrentSchema or ColumnDefaultKind.CurrentDatabase;

    internal void ValidateDestination(DbType type, int? length, int? precision, int? scale)
    {
        var compatible = Kind switch
        {
            ColumnDefaultKind.CurrentTimestamp or ColumnDefaultKind.CurrentUtcTimestamp =>
                type is DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset,
            ColumnDefaultKind.CurrentDate => type == DbType.Date,
            ColumnDefaultKind.CurrentTime => type == DbType.Time,
            ColumnDefaultKind.NewUuid => Representation switch
            {
                UuidRepresentation.Native => type == DbType.Guid,
                UuidRepresentation.Binary => type == DbType.Binary && length == 16,
                UuidRepresentation.CanonicalText => IsString(type)
                    && (type is DbType.StringFixedLength or DbType.AnsiStringFixedLength ? length == 36 : length >= 36),
                _ => false
            },
            _ => IsContext && IsString(type) && length > 0
        };
        if (!compatible)
            throw new ArgumentException($"The column type or facets are incompatible with the {Kind} default.");
    }

    private static bool IsString(DbType type)
        => type is DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength;
}
