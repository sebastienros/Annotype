# Getting started

This walkthrough mirrors the [`samples`](../samples) folder: three projects compiled separately.

| Project | Role | References |
|---|---|---|
| `Payments.Contracts` | Declares `IPlugin` and `PluginMetadataAttribute` | `Annotype` |
| `Payments.Plugins` | Implements `IPlugin` | `Annotype`, `Payments.Contracts` |
| `Payments.Host` | Registers registries, reads metadata | `Annotype`, `Payments.Plugins` |

Every project that declares contracts or implementations references `Annotype` **directly**:

```xml
<ItemGroup>
  <PackageReference Include="Annotype" Version="0.1.0-preview" />
</ItemGroup>
```

## 1. Contracts

```csharp
using Annotype;

namespace Payments.Contracts;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginMetadataAttribute : Attribute
{
    public PluginMetadataAttribute(string name) => Name = name;
    public string Name { get; }
    public int Priority { get; set; }
}

[RequiresMetadata(typeof(PluginMetadataAttribute))]
public interface IPlugin
{
    void Execute();
}
```

An interface can carry several `[RequiresMetadata]` attributes. Interfaces deriving from `IPlugin` inherit
its requirements.

## 2. Plugins

```csharp
[PluginMetadata("payments", Priority = 10)]
public sealed class PaymentPlugin : IPlugin
{
    public void Execute() { }
}
```

Forgetting the attribute fails the build with [`ANNO001`](diagnostics.md#anno001). Try it with the sample:

```bash
dotnet build samples/Payments.Plugins -p:AnnotypeDemoInvalidPlugin=true
```

When the project contains at least one retrievable entry, the generator adds
`Annotype.Generated.<AssemblyName>.MetadataRegistry` to it. Dots in the assembly name become underscores
(`Payments.Plugins` gives `Payments_Plugins`). Any other character also becomes an underscore and a stable
hash of the name is appended to avoid collisions. To see the generated file:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
```

### Richer property types

Attribute arguments are limited by C# to constants: primitives, `string`, enums, `Type`, `object` and arrays of
those. The properties an attribute *exposes* are not. The generated registry replays the attribute's constructor,
so the constructor can turn constants into any type. The sample's `PaymentLimitsAttribute` builds `Money`
instances (a custom class holding a `decimal`, which attributes cannot take directly):

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class PaymentLimitsAttribute : Attribute
{
    public PaymentLimitsAttribute(string minimum, string maximum)
    {
        Minimum = Money.Parse(minimum);
        Maximum = Money.Parse(maximum);
    }

    public Money Minimum { get; }
    public Money Maximum { get; }
}

[RequiresMetadata(typeof(PaymentLimitsAttribute))]
public interface IPaymentProcessor : IPlugin { }

[PluginMetadata("payments", Priority = 10)]
[PaymentLimits("0.50 EUR", "10000 EUR")]
public sealed class PaymentPlugin : IPaymentProcessor { /* ... */ }
```

Annotype captures the constant arguments, not the resulting objects, so:

- each `Metadata.Get` call runs the constructor again and returns new `Money` instances, mutable or not;
- the constructor should only depend on its arguments. Returning a shared instance, reading the clock or
  configuration, or having side effects makes the metadata unstable;
- conversion errors (for example `"1.x EUR"`) surface when the metadata is read, not at compile time.

## 3. Host

```csharp
using Annotype;

Metadata.Register(Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance);

var payments = Metadata.Get<PaymentPlugin, PluginMetadataAttribute>();

if (Metadata.TryGet<SomeType, PluginMetadataAttribute>(out var metadata))
{
    // ...
}
```

Register every plugin assembly's registry once at startup. Registration is thread-safe and idempotent.

### Dynamically loaded plugins

Annotype does not discover or load assemblies. A host that loads plugins at runtime needs a handshake, for
example a well-known entry point that each plugin implements and that returns its registry:

```csharp
public interface IPluginModule
{
    IMetadataRegistry Registry { get; }
}

public sealed class PaymentsModule : IPluginModule
{
    public IMetadataRegistry Registry => Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance;
}
```

### Native AOT and trimming

The runtime library is annotated as AOT-compatible on `net8.0`, and the generated registry only uses `typeof`
and constructors, so explicit registration works under trimming and Native AOT. CI publishes the sample host with Native AOT on
linux-x64 and runs it; support is still considered experimental.

```bash
dotnet publish samples/Payments.Host -r linux-x64 -p:AnnotypeSampleAot=true
```

## Running the sample

```bash
dotnet run --project samples/Payments.Host
```

```text
PaymentPlugin: Name=payments, Priority=10
PaymentPlugin: Minimum=0.50 EUR, Maximum=10000.00 EUR
RefundPlugin: Name=refunds, Priority=0
Program: no metadata
```

`RefundPlugin`'s constructor throws, which shows that reading metadata never creates an instance.
