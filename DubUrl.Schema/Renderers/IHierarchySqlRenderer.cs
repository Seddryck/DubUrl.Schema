using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

/// <summary>Optional provider integration for table-level hierarchies.</summary>
public interface IHierarchySqlRenderer
{
    /// <summary>Plans the physical schema additions for a validated node key.</summary>
    /// <remarks>Return null for an unsupported declaration or throw NotSupportedException with a diagnostic.</remarks>
    HierarchyStoragePlan? PlanHierarchy(IDialect dialect, HierarchyContext context);

    /// <summary>Renders a provider expression used by a hierarchy storage plan.</summary>
    string? RenderHierarchyExpression(IDialect dialect, Expression expression) => null;
}

public sealed record HierarchyContext(Table Table, Hierarchy Hierarchy, Column NodeKey, SchemaCreationOptions Options);

/// <summary>Columns and constraints added to the table by the provider's hierarchy plan.</summary>
/// <remarks>Report enforcement only when the generated schema checks parent existence on the target.</remarks>
public sealed record HierarchyStoragePlan(HierarchyRepresentation Representation, bool ParentExistenceEnforced,
    IReadOnlyList<Column> Columns, IReadOnlyList<Constraint> Constraints);