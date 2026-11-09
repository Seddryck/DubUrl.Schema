namespace DubUrl.Schema.Renderers;

public sealed record UniqueConstraintViewModel(string? Name, IReadOnlyList<Column> Columns, string? NullPolicyClause);
