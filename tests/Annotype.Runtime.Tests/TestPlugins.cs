using System;
using Annotype;

// These types are compiled with the Annotype generator, which emits
// Annotype.Generated.Annotype_Runtime_Tests.MetadataRegistry for this assembly.

namespace Annotype.Runtime.Tests.Contracts
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class WidgetAttribute : Attribute
    {
        public WidgetAttribute(string name) => Name = name;

        public string Name { get; }

        public int Order { get; set; }

        public string[]? Tags { get; set; }
    }

    [RequiresMetadata(typeof(WidgetAttribute))]
    public interface IWidget
    {
    }

    /// <summary>
    /// Records whether any widget code ran.
    /// </summary>
    public static class Tracker
    {
        public static int Constructions;
    }
}

namespace Annotype.Runtime.Tests.Widgets
{
    using Annotype.Runtime.Tests.Contracts;

    [Widget("button", Order = 1, Tags = ["input", "clickable"])]
    public sealed class Button : IWidget
    {
        static Button() => Tracker.Constructions++;

        public Button()
        {
            Tracker.Constructions++;
            throw new InvalidOperationException("Widgets must not be instantiated to read their metadata.");
        }
    }
}

namespace Other.Plugins
{
    // Same simple name as the sample's Payments.Plugins.PaymentPlugin, in another assembly and namespace.
    [Payments.Contracts.PluginMetadata("other-payments", Priority = 99)]
    public sealed class PaymentPlugin : Payments.Contracts.IPlugin
    {
        public void Execute()
        {
        }
    }
}
