using Annotype;

namespace Payments.Contracts;

/// <summary>
/// A plugin that moves money. Implementations need both <see cref="PluginMetadataAttribute"/> (inherited from
/// <see cref="IPlugin"/>) and <see cref="PaymentLimitsAttribute"/>.
/// </summary>
[RequiresMetadata(typeof(PaymentLimitsAttribute))]
public interface IPaymentProcessor : IPlugin
{
}
