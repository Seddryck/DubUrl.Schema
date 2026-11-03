using DubUrl.Querying.Dialects;

namespace DubUrl.Schema;

internal static class PhysicalOptionValidation
{
    internal static TablePhysicalOptions? Copy(TablePhysicalOptions? options, IEnumerable<string> columns)
    {
        if (options is null) return null;
        if (!Enum.IsDefined(options.Storage)) throw new ArgumentException("Unknown table storage.", nameof(options));
        var inventory = columns.ToHashSet(StringComparer.Ordinal);
        void Validate(IEnumerable<string> names)
        {
            var items = names.ToArray();
            if (items.Any(string.IsNullOrWhiteSpace) || items.Distinct(StringComparer.Ordinal).Count() != items.Length ||
                items.Any(name => !inventory.Contains(name)))
                throw new ArgumentException("Physical keys require distinct existing column names.", nameof(options));
        }

        Validate(options.ShardKey ?? []);
        Validate((options.SortKey ?? []).Select(column => column.Name));
        return options with
        {
            ShardKey = options.ShardKey is null ? null : Array.AsReadOnly(options.ShardKey.ToArray()),
            SortKey = options.SortKey is null ? null : Array.AsReadOnly(options.SortKey.ToArray())
        };
    }
}
