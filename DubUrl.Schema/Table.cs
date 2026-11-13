using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema;
public class Table
{
    public string Name { get; }
    public DatabaseObjectName Identity { get; }
    public OrderedImmutableDictionary<string, Column> Columns { get; }
    public ConstraintCollection Constraints { get; }
    public string? Description { get; }
    public IReadOnlyDictionary<string, string> Annotations { get; }
    public Hierarchy? Hierarchy { get; }
    public DubUrl.Querying.Dialects.TablePhysicalOptions? PhysicalOptions { get; }

    public Table(string name, Column[] columns, Constraint[]? constraints = null, string? description = null)
        : this(new DatabaseObjectName(name), columns, constraints, description)
    { }

    public Table(DatabaseObjectName identity, Column[] columns, Constraint[]? constraints = null, string? description = null)
        : this(identity, columns, constraints, description, null) { }

    public Table(DatabaseObjectName identity, Column[] columns, Constraint[]? constraints, string? description, DubUrl.Querying.Dialects.TablePhysicalOptions? physicalOptions)
        : this(identity, columns, constraints, description, physicalOptions, null) { }

    public Table(DatabaseObjectName identity, Column[] columns, Constraint[]? constraints, string? description,
        DubUrl.Querying.Dialects.TablePhysicalOptions? physicalOptions, IReadOnlyDictionary<string, string>? annotations)
        : this(identity, columns, constraints, description, physicalOptions, annotations, null) { }

    public Table(DatabaseObjectName identity, Column[] columns, Constraint[]? constraints, string? description,
        DubUrl.Querying.Dialects.TablePhysicalOptions? physicalOptions, IReadOnlyDictionary<string, string>? annotations,
        Hierarchy? hierarchy)
    {
        Hierarchy = hierarchy;
        Annotations = annotations?.ToImmutableSortedDictionary(StringComparer.Ordinal)
            ?? ImmutableSortedDictionary<string, string>.Empty;
        if (Annotations.Any(annotation => annotation.Value is null))
            throw new ArgumentException("Table annotations require string values.", nameof(annotations));
        PhysicalOptions = PhysicalOptionValidation.Copy(physicalOptions, columns.Select(column => column.Name));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Name = identity.Name;
        Columns = OrderedImmutableDictionary<string, Column>.From(
                    columns.Select(c => new KeyValuePair<string, Column>(c.Name, c)));

        var tableConstraints = NormalizePrimaryKey(constraints ?? []);
        Constraints = new(tableConstraints.Select(constraint => constraint is UniquenessConstraint unique
            ? NormalizeUniqueKey(unique) : constraint).ToArray());
        foreach (var column in Constraints.Get<PrimaryKeyConstraint>()?.Columns.Values ?? [])
        {
            if (column.StructuredType is not null)
                column.ValidateStructuredNullability(isPrimaryKey: true);
        }
        ValidateUniqueNames();
        ValidateUniqueTuples();
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private Constraint[] NormalizePrimaryKey(Constraint[] tableConstraints)
    {
        var primaryKeys = tableConstraints.OfType<PrimaryKeyConstraint>().ToArray();
        if (primaryKeys.Length > 1)
            throw new ArgumentException($"Table '{Name}' cannot contain more than one primary key.", "constraints");

        if (primaryKeys.Length == 1)
        {
            var primaryKey = primaryKeys[0];
            var unknownColumns = primaryKey.Columns.Keys.Where(column => !Columns.ContainsKey(column)).ToArray();
            if (unknownColumns.Length > 0)
            {
                throw new ArgumentException(
                    $"Primary key references unknown column(s) {string.Join(", ", unknownColumns)} in table '{Name}'.",
                    "constraints");
            }

            var normalizedPrimaryKey = new PrimaryKeyConstraint(
                primaryKey.Columns.Keys.Select(column => Columns[column]).ToArray(),
                primaryKey.Name);
            return tableConstraints
                .Select(constraint => ReferenceEquals(constraint, primaryKey) ? normalizedPrimaryKey : constraint)
                .ToArray();
        }
        return tableConstraints;
    }

    private UniquenessConstraint NormalizeUniqueKey(UniquenessConstraint unique)
    {
        if (unique.Columns.Count == 0 || unique.Columns.Any(column => !Columns.ContainsKey(column.Name)))
            throw new ArgumentException($"Table '{Name}' UNIQUE constraint requires existing columns.", "constraints");
        return new UniquenessConstraint(unique.Columns.Select(column => Columns[column.Name]).ToArray(), unique.Name, unique.NullPolicy);
    }

    internal IEnumerable<UniquenessConstraint> EffectiveTableUniqueConstraints()
    {
        var primaryColumns = PrimaryKeyColumns();
        var namedColumnKeys = Columns.Values
            .SelectMany(column => column.Constraints.OfType<UniquenessConstraint>()
                .Where(key => key.Name is not null)
                .Select(key => new UniquenessConstraint([column], key.Name)));
        return Constraints.OfType<UniquenessConstraint>().Concat(namedColumnKeys)
            .Where(unique => !primaryColumns.SetEquals(unique.Columns.Select(column => column.Name)));
    }

    internal HashSet<string> PrimaryKeyColumns()
        => Constraints.Get<PrimaryKeyConstraint>()?.Columns.Keys.ToHashSet(StringComparer.Ordinal)
            ?? Columns.Values.Where(column => column.Constraints.Get<PrimaryKeyConstraint>() is not null)
                .Select(column => column.Name).ToHashSet(StringComparer.Ordinal);

    private void ValidateUniqueNames()
    {
        var columnConstraints = Columns.Values.SelectMany(column => column.Constraints.OfType<Constraint>()).ToArray();
        var nameCollision = Constraints.OfType<Constraint>().Concat(columnConstraints)
            .Where(constraint => constraint.Name is not null)
            .GroupBy(constraint => constraint.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1 && group.Any(constraint => constraint is UniquenessConstraint));
        if (nameCollision is not null)
            throw new ArgumentException($"Constraint name '{nameCollision.Key}' is ambiguous on table '{Name}'.");
    }

    private IEnumerable<UniquenessConstraint> GetInlineUniqueTuples()
    {
        var primaryColumns = PrimaryKeyColumns();
        foreach (var column in Columns.Values)
        {
            var unique = column.Constraints.OfType<UniquenessConstraint>().ToArray();
            if (unique.Length > 1 || unique.Any(key => key.Columns.Count > 0))
                throw new ArgumentException($"Column '{column.Name}' on table '{Name}' has incompatible inline UNIQUE declarations.");
            if (unique is [{ Name: null }] && !primaryColumns.SetEquals([column.Name]))
                yield return new UniquenessConstraint([column]);
        }
    }

    private void ValidateUniqueTuples()
    {
        var keys = EffectiveTableUniqueConstraints().Concat(GetInlineUniqueTuples()).ToArray();
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            var members = key.Columns.Select(column => column.Name).ToArray();
            var memberSet = members.ToHashSet(StringComparer.Ordinal);
            foreach (var other in keys.Take(index))
            {
                ValidateUniquePair(key, other, members, memberSet);
            }
        }
    }

    private void ValidateUniquePair(UniquenessConstraint key, UniquenessConstraint other, string[] members, HashSet<string> memberSet)
    {
        var otherMembers = other.Columns.Select(column => column.Name);
        if (key.NullPolicy != other.NullPolicy && memberSet.SetEquals(otherMembers))
            throw new ArgumentException($"Table '{Name}' has conflicting UNIQUE null policies for the same tuple.");
        if (key.Name is null && other.Name is null && members.SequenceEqual(otherMembers, StringComparer.Ordinal))
            throw new ArgumentException($"Table '{Name}' has ambiguous unnamed UNIQUE declarations for the same tuple.");
    }
}
