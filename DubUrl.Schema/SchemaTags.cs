using System.Collections.Immutable;

namespace DubUrl.Schema;

internal static class SchemaTags
{
    internal static IReadOnlyDictionary<string, string> Empty { get; }
        = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    internal static bool IsValid(KeyValuePair<string, string> tag)
        => !string.IsNullOrWhiteSpace(tag.Key) && tag.Value is not null;

    internal static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Any(tag => !IsValid(tag)))
            throw new ArgumentException("Tags require nonblank names and string values.", nameof(tags));

        return tags.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }
}
