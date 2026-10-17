using Payments.Contracts;

namespace Payments.Plugins;

[PluginMetadata("payments", Priority = 10)]
public sealed class PaymentPlugin : IPlugin
{
    public void Execute() => Console.WriteLine("Processing payment");
}
