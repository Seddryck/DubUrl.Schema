using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

/// <summary>Optional capability for dialects that render scripts from an ordered command plan.</summary>
public interface ISchemaCommandSqlRenderer
{
    /// <summary>Renders clauses between the closing table definition and its comment.</summary>
    string RenderTableOptions(IDialect dialect, bool hasDefaults);

    /// <summary>Plans table and column checks after the table is created.</summary>
    IReadOnlyList<SchemaSqlCommand> PlanTableChecks(IDialect dialect, DatabaseObjectName table,
        IReadOnlyList<SchemaCheckDefinition> checks);

    /// <summary>Validates the target and plans any prerequisite commands to run before schema changes.</summary>
    /// <param name="dialect">The SQL dialect.</param>
    /// <param name="key">The foreign-key declaration, including the source-to-target column pairing.</param>
    /// <param name="createdTarget">The planned table definition, or null when the table already exists.</param>
    IReadOnlyList<SchemaSqlCommand> PlanForeignKeyPrerequisites(IDialect dialect, ForeignKeyConstraint key,
        TableDefinitionContext? createdTarget) => [];
}

/// <param name="Name">The declared constraint name, if supplied.</param>
/// <param name="Predicate">The trusted SQL predicate rendered from the schema model.</param>
public sealed record SchemaCheckDefinition(string? Name, string Predicate);
