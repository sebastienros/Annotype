using Payments.Contracts;

namespace Payments.Plugins;

[PluginMetadata("refunds")]
public sealed class RefundPlugin : IPlugin
{
    public RefundPlugin() => throw new InvalidOperationException("Reading metadata must not create plugin instances.");

    public void Execute() => Console.WriteLine("Processing refund");
}
