using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Annotype.Runtime.Tests.Contracts;
using Annotype.Runtime.Tests.Widgets;
using Payments.Contracts;
using Xunit;

namespace Annotype.Runtime.Tests;

public class MetadataTests
{
    static MetadataTests()
    {
        // Registration is process-wide; registering the same instances from every test is a no-op.
        Metadata.Register(Annotype.Generated.Annotype_Runtime_Tests.MetadataRegistry.Instance);
        Metadata.Register(Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance);
    }

    [Fact]
    public void GetReturnsMetadataFromRegistryGeneratedInPluginAssembly()
    {
        var metadata = Metadata.Get<Payments.Plugins.PaymentPlugin, PluginMetadataAttribute>();

        Assert.Equal("payments", metadata.Name);
        Assert.Equal(10, metadata.Priority);
    }

    [Fact]
    public void PropertiesCanBeCustomClassesBuiltByTheConstructor()
    {
        var first = Metadata.Get<Payments.Plugins.PaymentPlugin, PaymentLimitsAttribute>();
        var second = Metadata.Get<Payments.Plugins.PaymentPlugin, PaymentLimitsAttribute>();

        Assert.Equal(0.50m, first.Minimum.Amount);
        Assert.Equal("EUR", first.Minimum.Currency);
        Assert.Equal(10000m, first.Maximum.Amount);

        // The constructor runs for every snapshot, so custom instances are never shared.
        Assert.NotSame(first.Minimum, second.Minimum);
    }

    [Fact]
    public void GetDoesNotInstantiateTheImplementation()
    {
        var before = Volatile.Read(ref Tracker.Constructions);

        var metadata = Metadata.Get<Button, WidgetAttribute>();
        var refunds = Metadata.Get<Payments.Plugins.RefundPlugin, PluginMetadataAttribute>();

        Assert.Equal("button", metadata.Name);
        Assert.Equal(1, metadata.Order);
        Assert.Equal(["input", "clickable"], metadata.Tags!);
        Assert.Equal("refunds", refunds.Name);
        Assert.Equal(before, Volatile.Read(ref Tracker.Constructions));
    }

    [Fact]
    public void SimilarlyNamedTypesInDifferentAssembliesDoNotCrossMatch()
    {
        var sample = Metadata.Get<Payments.Plugins.PaymentPlugin, PluginMetadataAttribute>();
        var other = Metadata.Get<Other.Plugins.PaymentPlugin, PluginMetadataAttribute>();

        Assert.Equal("payments", sample.Name);
        Assert.Equal("other-payments", other.Name);
        Assert.Equal(99, other.Priority);
    }

    [Fact]
    public void GetReturnsDistinctSnapshotOnEveryCall()
    {
        var first = Metadata.Get<Button, WidgetAttribute>();
        var second = Metadata.Get<Button, WidgetAttribute>();

        Assert.NotSame(first, second);
        Assert.NotSame(first.Tags, second.Tags);

        first.Order = 42;
        first.Tags![0] = "mutated";

        var third = Metadata.Get<Button, WidgetAttribute>();
        Assert.Equal(1, second.Order);
        Assert.Equal(1, third.Order);
        Assert.Equal("input", third.Tags![0]);
    }

    [Fact]
    public void MissingMetadataFailsWithMeaningfulException()
    {
        Assert.False(Metadata.TryGet<MetadataTests, PluginMetadataAttribute>(out var metadata));
        Assert.Null(metadata);

        // Exact attribute type: a registered type asked for another attribute is not found.
        Assert.False(Metadata.TryGet<Button, PluginMetadataAttribute>(out _));
        Assert.False(Metadata.TryGet<Button, Attribute>(out _));

        var exception = Assert.Throws<MetadataNotFoundException>(Metadata.Get<MetadataTests, PluginMetadataAttribute>);
        Assert.Equal(typeof(MetadataTests), exception.ImplementationType);
        Assert.Equal(typeof(PluginMetadataAttribute), exception.AttributeType);
        Assert.Contains(typeof(MetadataTests).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(PluginMetadataAttribute).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Metadata.Register", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonGenericOverloadsMatchGenericOnes()
    {
        var metadata = Assert.IsType<WidgetAttribute>(Metadata.Get(typeof(Button), typeof(WidgetAttribute)));
        Assert.Equal("button", metadata.Name);
        Assert.True(Metadata.TryGet(typeof(Button), typeof(WidgetAttribute), out var untyped));
        Assert.IsType<WidgetAttribute>(untyped);
        Assert.Throws<MetadataNotFoundException>(() => Metadata.Get(typeof(string), typeof(WidgetAttribute)));
        Assert.Throws<ArgumentNullException>(() => Metadata.TryGet(null!, typeof(WidgetAttribute), out _));
    }
}

public class MetadataRegistrySetTests
{
    private sealed class FakeRegistry(Type implementationType, Type attributeType, Func<Attribute> factory) : IMetadataRegistry
    {
        public int Calls;

        public bool TryGet(Type requestedImplementation, Type requestedAttribute, [NotNullWhen(true)] out Attribute? metadata)
        {
            Interlocked.Increment(ref Calls);

            if (requestedImplementation == implementationType && requestedAttribute == attributeType)
            {
                metadata = factory();
                return true;
            }

            metadata = null;
            return false;
        }
    }

    private sealed class Implementation;

    [Fact]
    public void UnregisteredRegistryIsNotQueried()
    {
        var set = new MetadataRegistrySet();

        Assert.False(set.TryGet(typeof(Button), typeof(WidgetAttribute), out _));
    }

    [Fact]
    public void RegistrationIsIdempotentForSameInstance()
    {
        var set = new MetadataRegistrySet();
        var registry = new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute("x"));

        set.Register(registry);
        set.Register(registry);

        Assert.Single(set.Registries);
        Assert.True(set.TryGet(typeof(Implementation), typeof(ObsoleteAttribute), out var metadata));
        Assert.Equal("x", Assert.IsType<ObsoleteAttribute>(metadata).Message);
        Assert.Equal(1, registry.Calls);
    }

    [Fact]
    public void RegisteringNullThrows()
        => Assert.Throws<ArgumentNullException>(() => new MetadataRegistrySet().Register(null!));

    [Fact]
    public void CollidingRegistriesAreRejected()
    {
        var set = new MetadataRegistrySet();
        set.Register(new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute("first")));
        set.Register(new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute("second")));

        var exception = Assert.Throws<AmbiguousMetadataException>(() => set.TryGet(typeof(Implementation), typeof(ObsoleteAttribute), out _));
        Assert.Equal(typeof(Implementation), exception.ImplementationType);
        Assert.Contains(nameof(FakeRegistry), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryReturningWrongTypeIsRejected()
    {
        var set = new MetadataRegistrySet();
        set.Register(new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new SerializableAttribute()));

        Assert.Throws<InvalidOperationException>(() => set.TryGet(typeof(Implementation), typeof(ObsoleteAttribute), out _));
    }

    [Fact]
    public void ConcurrentRegistrationAndLookupAreSafe()
    {
        var set = new MetadataRegistrySet();
        var known = new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute("known"));
        set.Register(known);

        // 64 registries, each owning a distinct implementation type.
        var keys = typeof(object).Assembly.GetExportedTypes().Where(static t => t.IsClass).Take(64).ToArray();
        var registries = keys
            .Select(key => new FakeRegistry(key, typeof(ObsoleteAttribute), () => new ObsoleteAttribute(key.FullName)))
            .ToArray();
        var failures = 0;

        Parallel.For(0, 20_000, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken }, i =>
        {
            var index = i % registries.Length;
            set.Register(registries[index]);

            if (!set.TryGet(typeof(Implementation), typeof(ObsoleteAttribute), out var metadata)
                || ((ObsoleteAttribute)metadata).Message != "known"
                || !set.TryGet(keys[index], typeof(ObsoleteAttribute), out var registered)
                || ((ObsoleteAttribute)registered).Message != keys[index].FullName)
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
        Assert.Equal(registries.Length + 1, set.Registries.Length);
    }

    [Fact]
    public void LookupOrderIsRegistrationOrder()
    {
        var set = new MetadataRegistrySet();
        var first = new FakeRegistry(typeof(Implementation), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute());
        var second = new FakeRegistry(typeof(int), typeof(ObsoleteAttribute), static () => new ObsoleteAttribute());

        set.Register(first);
        set.Register(second);

        Assert.Equal([first, second], set.Registries);
    }
}
