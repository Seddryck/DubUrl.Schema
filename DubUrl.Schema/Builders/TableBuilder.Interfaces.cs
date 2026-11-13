using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DubUrl.Schema.Builders;

public interface ITableColumnCollectionBuilder
{
    ITableConstraintCollectionBuilder WithColumns(Func<ColumnCollectionBuilder, ColumnCollectionBuilder> columns);
}

public interface ITableConstraintCollectionBuilder : ITableBuilder
{
    ITableBuilder WithConstraints(Func<TableConstraintCollectionBuilder, TableConstraintCollectionBuilder> constraints);
}

public interface ITableBuilder
{
    ITableBuilder WithDescription(string? description);
    ITableBuilder WithPhysicalOptions(DubUrl.Querying.Dialects.TablePhysicalOptions options);
    ITableBuilder WithAnnotations(IReadOnlyDictionary<string, string> annotations)
        => throw new NotSupportedException("This table builder does not support annotations.");
    ITableBuilder WithHierarchy(Hierarchy hierarchy)
        => throw new NotSupportedException("This table builder does not support hierarchies.");
    Table Build();
}
