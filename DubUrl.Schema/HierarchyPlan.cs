namespace DubUrl.Schema;

/// <summary>The physical representation chosen for a table hierarchy.</summary>
public enum HierarchyRepresentation
{
    ParentId,
    HierarchyId
}

/// <summary>Reports the outcome of planning a hierarchy for the selected target.</summary>
public sealed record HierarchyPlan(DatabaseObjectName Table, HierarchyRepresentation? Representation,
    bool ParentExistenceEnforced, string? Diagnostic)
{
    public bool IsSupported => Representation is not null && Diagnostic is null;
}