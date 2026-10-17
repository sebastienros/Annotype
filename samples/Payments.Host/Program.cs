using Annotype;
using Payments.Contracts;
using Payments.Plugins;

// Composition root: registries generated in plugin assemblies are registered explicitly.
Metadata.Register(Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance);

// Reads compile-time captured values: no plugin instance, no attribute reflection.
var payments = Metadata.Get<PaymentPlugin, PluginMetadataAttribute>();
Console.WriteLine($"{nameof(PaymentPlugin)}: Name={payments.Name}, Priority={payments.Priority}");

// RefundPlugin's constructor throws, which proves no instance is created.
var refunds = Metadata.Get<RefundPlugin, PluginMetadataAttribute>();
Console.WriteLine($"{nameof(RefundPlugin)}: Name={refunds.Name}, Priority={refunds.Priority}");

if (!Metadata.TryGet<Program, PluginMetadataAttribute>(out _))
{
    Console.WriteLine($"{nameof(Program)}: no metadata");
}
