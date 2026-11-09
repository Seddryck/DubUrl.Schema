using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

public class IndexViewModel
{
    public bool ConsumedByTable { get; internal set; }
    public DubUrl.Querying.Dialects.SchemaIndexStructure Structure { get; }
    public string Name { get; }
    public string TableName { get; }
    public DatabaseObjectName Identity { get; }
    public DatabaseObjectName TableIdentity { get; }
    public IndexColumnViewModel[] Columns { get; }
    public bool IsUnique { get; }
    public string? FilterPredicate { get; internal set; }
    public string? KeySuffix { get; internal set; }

    public IndexViewModel(Index index)
    {
        Name = index.Name;
        Structure = index.Structure;
        TableName = index.TableName;
        Identity = index.Identity;
        TableIdentity = index.TableIdentity;
        IsUnique = index.IsUnique;
        Columns = index.Columns.Values.Select(c => new IndexColumnViewModel(c)).ToArray();
    }
}
