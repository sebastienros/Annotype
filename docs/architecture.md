# Architecture

Annotype has three parts, shipped in one NuGet package:

| Part | Assembly | Package path | Runs in |
|---|---|---|---|
| Runtime API | `Annotype.dll` (`netstandard2.0`, `net8.0`) | `lib/<tfm>/` | The application |
| Analyzer | `Annotype.Tooling.dll` (`netstandard2.0`) | `analyzers/dotnet/cs/` | The compiler |
| Source generator | same as the analyzer | same | The compiler |

```text
Payments.Contracts            Payments.Plugins                    Payments.Host
------------------            ----------------                    -------------
[RequiresMetadata(...)]  -->  [PluginMetadata("payments")]        Metadata.Register(
interface IPlugin             class PaymentPlugin : IPlugin         ...Payments_Plugins.MetadataRegistry.Instance)
                                    |                             Metadata.Get<PaymentPlugin,
                              analyzer: ANNO001/ANNO002              PluginMetadataAttribute>()
                              generator: MetadataRegistry  ---->         |
                                                                  registry: new PluginMetadataAttribute(
                                                                    "payments") { Priority = 10 }
```

## Runtime

- `RequiresMetadataAttribute` is the only declaration API. It uses `typeof(...)`, not generic attributes, so
  contracts can be written in any C# version.
- `IMetadataRegistry.TryGet(Type, Type, out Attribute?)` is the contract between generated code and the runtime.
- `Metadata` holds a process-wide, copy-on-write array of registries (`MetadataRegistrySet`). Registration takes
  a lock and replaces the array; lookups read it without locking. Registering the same instance twice is a
  no-op, and the lookup order is the registration order.
- A lookup queries **every** registry. If two return a value for the same pair, `AmbiguousMetadataException` is
  thrown. Registries do not expose their keys, so collisions are detected at lookup rather than registration.
- Lookups use exact types. Asking for a base attribute type or a base implementation type does not match.

The runtime never calls `GetCustomAttributes`, never enumerates assemblies, and never instantiates the
implementation type.

## Analyzer

`MetadataContractAnalyzer` registers a symbol action on named types:

- **Interfaces**: each `[RequiresMetadata]` is validated (`ANNO002`).
- **Concrete classes** (non-abstract, non-static, including record classes): every interface in
  `AllInterfaces` is inspected, which covers interfaces implemented by base classes and inherited interfaces.
  Requirements are grouped by attribute type with `SymbolEqualityComparer.Default`, so one `ANNO001` is reported
  per missing attribute regardless of how many contracts require it. `partial` types are a single symbol, so
  they are reported once on their first declaration.

Satisfaction is checked with `INamedTypeSymbol.GetAttributes()`, which only returns attributes declared on the
type itself. This is the direct-only policy: inherited attributes are deliberately ignored.

## Generator

`MetadataRegistryGenerator` is an `IIncrementalGenerator`:

1. A syntax provider selects class and record class declarations. The transform resolves the symbol, skips
   non-first `partial` declarations, and resolves requirements with the same code as the analyzer.
2. For each required attribute present on the type, `AttributeEmitter` builds a C# expression
   `new TAttribute(ctorArgs) { Named = value }` from `AttributeData`. This is the same sequence the runtime
   follows when it materializes a custom attribute, so the generator never needs to know default values or
   field initializers of the attribute type.
3. The transform outputs an equatable model (strings and file locations only, no symbols), so unrelated edits
   are served from the incremental cache.
4. The models are sorted by fully qualified name and rendered into one file, `Annotype.MetadataRegistry.g.cs`.
   No file is generated when the assembly has no retrievable metadata.

The generated code:

- is valid C# 7.3 and nullable-oblivious, so it compiles in any consumer configuration without warnings;
- uses `global::` for every type and escapes keyword identifiers;
- emits literals with exact types (`5L`, `(byte)(5)`, `1.5D`, `float.NaN`, `int.MinValue`), enums as casts of their
  underlying value (which preserves flags and undefined values), `typeof` for types (including
  `typeof(List<>)`), and `new T[] { ... }` for arrays, casting to the parameter type when it differs (for example
  `object`);
- contains no reflection, `Activator` or `dynamic`.

### What is not generated

| Case | Result |
|---|---|
| Missing required attribute | Nothing; the analyzer reports `ANNO001` |
| Private, protected or file-local implementation, or inaccessible argument | `ANNO003`, entry skipped |
| Generic type or type nested in a generic type | `ANNO004` warning, entry skipped |
| Required attribute with `AllowMultiple = true` | `ANNO005`, entry skipped |
| Attribute that does not bind | Nothing; the compiler reports it |

## Why explicit registration

A source generator only adds code to the compilation it runs in. It cannot add members to `Annotype.dll`, and it
cannot see assemblies compiled later (plugins). Each assembly therefore gets its own registry, and the host
registers them. This is also what keeps the design trimming and AOT friendly: no assembly scanning, no
module-initializer side effects. An opt-in module initializer is a possible future convenience.

## Why one package

Analyzers must be in the compilation of the project that declares the implementation. Shipping the tooling with
the runtime means the natural `PackageReference` is enough. Whether analyzers also reach a project through
another package or project depends on NuGet and SDK behavior and on how that dependency references Annotype (for
example `PrivateAssets="all"`), so Annotype does not rely on it: every implementation project should reference
Annotype directly. `tests/Annotype.Packaging.Tests` verifies both the direct and the private-dependency cases.

## Compatibility

- The tooling is compiled against Roslyn 4.8 (`Microsoft.CodeAnalysis.CSharp` 4.8.0), the compiler of the .NET 8.0.100
  SDK. Newer compilers load it; older ones fail with `CS9057`. CI builds a consumer with SDK 8.0.100 exactly.
- Raising the Roslyn version raises the minimum SDK. Update `tests/Annotype.Packaging.Tests/MinimumSdk/global.json`
  and the README together.

## Decisions to revisit

- Immutable metadata projections instead of mutable attribute snapshots.
- An opt-in strict mode requiring metadata on abstract classes.
- Inheritance policies (`Direct`, `Inherit`, `Override`, `Merge`) instead of direct-only.
- Code fix adding the missing attribute.
- Registry key exposure to detect collisions at registration time.
