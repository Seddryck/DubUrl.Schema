using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Documents;

internal static class UniqueNullPolicyNames
{
    public static bool IsValid(string? value) => value is null or "native" or "nulls-distinct" or "nulls-not-distinct";

    public static UniqueNullPolicy Parse(string? value) => value switch
    {
        null or "native" => UniqueNullPolicy.Native,
        "nulls-distinct" => UniqueNullPolicy.NullsDistinct,
        "nulls-not-distinct" => UniqueNullPolicy.NullsNotDistinct,
        _ => throw new ArgumentException("Unknown UNIQUE null policy.", nameof(value))
    };

    public static string? Format(UniqueNullPolicy value) => value switch
    {
        UniqueNullPolicy.Native => null,
        UniqueNullPolicy.NullsDistinct => "nulls-distinct",
        UniqueNullPolicy.NullsNotDistinct => "nulls-not-distinct",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
