#if ANNOTYPE_DEMO_INVALID_PLUGIN
using Payments.Contracts;

namespace Payments.Plugins;

// error ANNO001: Type 'Payments.Plugins.InvalidPlugin' implements 'Payments.Contracts.IPlugin'
// but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'
public sealed class InvalidPlugin : IPlugin
{
    public void Execute()
    {
    }
}
#endif
