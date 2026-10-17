using Annotype;

namespace Payments.Contracts;

/// <summary>
/// Every concrete implementation must be annotated with <see cref="PluginMetadataAttribute"/>.
/// </summary>
[RequiresMetadata(typeof(PluginMetadataAttribute))]
public interface IPlugin
{
    void Execute();
}
