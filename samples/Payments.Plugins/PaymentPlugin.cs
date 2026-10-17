using Payments.Contracts;

namespace Payments.Plugins;

[PluginMetadata("payments", Priority = 10)]
[PaymentLimits("0.50 EUR", "10000 EUR")]
public sealed class PaymentPlugin : IPaymentProcessor
{
    public void Execute() => Console.WriteLine("Processing payment");
}
