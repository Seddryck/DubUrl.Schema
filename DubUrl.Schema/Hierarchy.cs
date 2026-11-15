namespace DubUrl.Schema;

/// <summary>Declares a parent–child hierarchy over a table's single-column primary key.</summary>
/// <remarks>
/// Parent-reference storage uses a null parent for each root, so multiple roots are allowed.
/// A foreign key enforces parent existence but does not detect cycles.
/// </remarks>
public sealed record Hierarchy
{
    public Hierarchy(string nodeKey, bool requireParentExists = true, string parentColumnName = "parent_id")
    {
        NodeKey = ValidateName(nodeKey, nameof(nodeKey));
        ParentColumnName = ValidateName(parentColumnName, nameof(parentColumnName));
        RequireParentExists = requireParentExists;
    }

    public string NodeKey { get; }
    public string ParentColumnName { get; }
    /// <summary>Whether generation must provide a constraint that enforces parent existence.</summary>
    /// <value>Defaults to true. False permits storage without enforcement if the target cannot enforce it.</value>
    public bool RequireParentExists { get; }

    private static string ValidateName(string name, string parameter)
        => string.IsNullOrWhiteSpace(name) || name.Contains('\0')
            ? throw new ArgumentException("A hierarchy column name must be nonblank and cannot contain NUL.", parameter)
            : name;
}