using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DubUrl.Querying.Dialects;

namespace DubUrl.Schema;
public class SchemaScriptDeployer
{
    /// <summary>Executes an arbitrary caller-supplied string as one provider command. No script parsing is performed.</summary>
    public virtual void DeploySchema(ConnectionUrl connectionUrl, string script)
    {
        using var conn = connectionUrl.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = script;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Executes an ordered plan on one connection, stopping at the first failure.</summary>
    /// <remarks>No transaction or compensating rollback is implied for provider DDL.</remarks>
    public virtual void DeploySchema(ConnectionUrl connectionUrl, IReadOnlyList<SchemaSqlCommand> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var commands = plan.ToArray();
        foreach (var command in commands)
        {
            if (command is null || string.IsNullOrWhiteSpace(command.CommandText))
                throw new ArgumentException("A deployment plan must contain nonempty commands.", nameof(plan));
        }
        if (commands.Length == 0) return;
        using var connection = connectionUrl.Open();
        for (var i = 0; i < commands.Length; i++)
        {
            var operation = commands[i];
            using var command = connection.CreateCommand();
            command.CommandText = operation.CommandText;
            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                // Preserve the provider exception type/code/stack for existing callers. Do not attach SQL or URLs.
                exception.Data["SchemaCommandIndex"] = i;
                exception.Data["SchemaCommandOperation"] = operation.Operation.ToString();
                if (operation.ObjectName is not null) exception.Data["SchemaCommandObject"] = operation.ObjectName;
                throw;
            }
        }
    }
}
