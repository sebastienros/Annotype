![Annotype](assets/banner.png)

# Annotype

[![NuGet](https://img.shields.io/nuget/v/Annotype.svg?label=nuget.org)](https://www.nuget.org/packages/Annotype)
[![Feedz preview version](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Fsebastienros%2Fannotype%2Fshield%2FAnnotype%2Flatest)](https://f.feedz.io/sebastienros/annotype/nuget/index.json)
[![BSD-3-Clause](https://img.shields.io/github/license/sebastienros/annotype)](https://github.com/sebastienros/annotype/blob/main/LICENSE)

**Move invariants from instances to types.**

Annotype is a Roslyn-powered framework for type-level metadata in C#. A contract (an interface or a base class)
declares which attributes its implementations must carry. An analyzer turns a missing attribute into a compile-time error, and
a source generator captures the attribute values into a registry so they can be read back **without
reflection and without creating an instance** of the annotated type.

A plugin's name and priority describe the `PaymentPlugin` *type*, not each `PaymentPlugin` object. Annotype
lets you say so, enforce it, and read it cheaply.

> Status: preview (0.1). The API and diagnostics may change before 1.0.

## Getting started

Reference the package from **every project that declares contracts or implementations**:

```xml
<PackageReference Include="Annotype" Version="0.1.0-preview" />
```

One package brings the runtime API (`lib/`) and the analyzer and generator (`analyzers/dotnet/cs/`).

### NuGet feeds

| Source | Purpose | Package feed |
| --- | --- | --- |
| Tagged releases (`v*`) | Stable releases. | [![NuGet.org stable version](https://img.shields.io/nuget/v/Annotype.svg?label=nuget.org)](https://www.nuget.org/packages/Annotype) |
| [`main`](https://github.com/sebastienros/annotype/tree/main) | Latest development changes. | [![Feedz preview version](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Fsebastienros%2Fannotype%2Fshield%2FAnnotype%2Flatest)](https://f.feedz.io/sebastienros/annotype/nuget/index.json) |

### Preview packages

Every successful build on `main` publishes a preview package to the
[Annotype feed on feedz.io](https://f.feedz.io/sebastienros/annotype/nuget/index.json).
Versions follow `0.1.0-preview-<run number>`, using the GitHub Actions build run number.
These packages contain the latest development changes and are intended for testing before release.

Add the preview feed alongside NuGet.org, then install the latest prerelease version:

```shell
dotnet nuget add source https://f.feedz.io/sebastienros/annotype/nuget/index.json --name annotype-preview
dotnet add package Annotype --prerelease
```

### 1. Declare a contract

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

### 2. Implement it, possibly in another assembly

```csharp
using Payments.Contracts;

namespace Payments.Plugins;

[PluginMetadata("payments", Priority = 10)]
public sealed class PaymentPlugin : IPlugin
{
    public void Execute() { }
}

// error ANNO001: Type 'Payments.Plugins.InvalidPlugin' implements 'Payments.Contracts.IPlugin'
// but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'
public sealed class InvalidPlugin : IPlugin
{
    public void Execute() { }
}
```

The generator emits `Annotype.Generated.Payments_Plugins.MetadataRegistry` into the plugin assembly.

### 3. Register and read metadata in the host

```csharp
using Annotype;

Metadata.Register(Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance);

PluginMetadataAttribute data = Metadata.Get<PaymentPlugin, PluginMetadataAttribute>();
Console.WriteLine(data.Name);     // payments
Console.WriteLine(data.Priority); // 10
```

`Metadata.Get` never calls `GetCustomAttributes`, never scans assemblies and never instantiates
`PaymentPlugin`. The generated registry recreates the attribute with `new PluginMetadataAttribute("payments")
{ Priority = 10 }`.

## Runtime API

| Member | Behavior |
|---|---|
| `Metadata.Register(IMetadataRegistry)` | Adds a registry. Registering the same instance twice is a no-op. Thread-safe. |
| `Metadata.Get<TImplementation, TAttribute>()` | Returns a new attribute snapshot, or throws `MetadataNotFoundException`. |
| `Metadata.TryGet<TImplementation, TAttribute>(out TAttribute?)` | Returns `false` when no registry provides the metadata. |
| `Metadata.Get(Type, Type)` / `Metadata.TryGet(Type, Type, out Attribute?)` | Non-generic equivalents. |

Lookups match the **exact** implementation and attribute types. If two registered registries provide the same
pair, lookups throw `AmbiguousMetadataException` instead of picking one.

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| [ANNO001](docs/diagnostics.md#anno001) | Error | A concrete implementation is missing a required attribute. |
| [ANNO002](docs/diagnostics.md#anno002) | Error | A `[RequiresMetadata]` contract is invalid. |
| [ANNO003](docs/diagnostics.md#anno003) | Error | An attribute cannot be recreated by generated code. |
| [ANNO004](docs/diagnostics.md#anno004) | Warning | A generic implementation is excluded from the generated registry. |
| [ANNO005](docs/diagnostics.md#anno005) | Error | The required attribute allows multiple instances, which the registry does not support. |

## Limitations

These are deliberate in this version. See [docs/architecture.md](docs/architecture.md) for the reasons.

- **Direct package reference required.** NuGet does not flow analyzers transitively. A plugin project that only
  references a contracts library (which references Annotype) compiles **without** enforcement or a registry.
  Add `<PackageReference Include="Annotype" />` to every implementation project. Enforcement only covers
  compilations that run the analyzer, and diagnostics can be suppressed like any other: this is a build-time
  guard, not a security boundary.
- **Explicit registration.** A source generator cannot modify the Annotype runtime or other assemblies. The host
  registers each assembly's `MetadataRegistry.Instance`. Dynamically loaded plugins need their own registration
  handshake.
- **Direct-only metadata.** The attribute must be declared on the concrete class itself, even when the attribute
  type has `Inherited = true`. A derived class of an annotated class must declare it again.
- **Concrete classes only.** Abstract classes, structs and record structs are not validated. Record classes are.
- **Snapshots are mutable.** `Get` returns a new attribute instance on every call. Mutating it has no effect on
  other snapshots, but nothing makes it immutable.
- **No generic implementations in the registry.** Generic types, and types nested in generic types, are validated
  but excluded from the registry with `ANNO004`. Derive a non-generic class to make the metadata retrievable.
- **Single-instance attributes.** Required attributes must be declared with `AllowMultiple = false`.
- **Process-wide registration.** Registries cannot be removed and keep their assembly alive. Collectible
  `AssemblyLoadContext` unloading is not supported.

## Supported attribute values

Constructor arguments and public named properties or fields of the following kinds are captured:
`bool`, `char`, `string`, all integral types, `float`, `double`, enums (including undefined and flag values),
`typeof(...)` (including open generic types such as `typeof(List<>)`), `null`, `object` holding any of these,
and single-dimensional arrays of them. These are C#'s limits on attribute arguments; the properties an attribute
exposes can be of any type built by its constructor (see
[richer property types](docs/getting-started.md#richer-property-types)). Values that cannot be recreated from the implementation's assembly (for
example `typeof` of a private nested type) are reported with `ANNO003`.

## Compatibility

- The runtime targets `netstandard2.0` and `net8.0` (trimming and Native AOT annotated).
- The tooling requires the Roslyn 4.8 compiler or newer: .NET SDK 8.0.100 or Visual Studio 17.8.
- Generated code is valid C# 7.3, so projects using an older `LangVersion` work too.

## Building from source

```bash
dotnet build
dotnet test
```

Run the sample, then see the build fail on a missing attribute:

```bash
dotnet run --project samples/Payments.Host
dotnet build samples/Payments.Plugins -p:AnnotypeDemoInvalidPlugin=true
```

`tests/Annotype.Packaging.Tests` packs the library and builds isolated consumer projects against the package; it
needs network access to nuget.org.

## License

BSD-3-Clause
