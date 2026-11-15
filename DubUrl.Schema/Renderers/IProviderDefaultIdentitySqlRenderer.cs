using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Renders identity generation governed by the target provider's settings.</summary>
public interface IProviderDefaultIdentitySqlRenderer
{
    /// <summary>Renders the column type and its identity clause without seed or increment overrides.</summary>
    string RenderProviderDefaultIdentity(IDialect dialect, string type);
}
