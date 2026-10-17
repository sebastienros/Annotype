# Diagnostics

Annotype reports diagnostics from two components shipped in the same package:

- the **analyzer** (`MetadataContractAnalyzer`) enforces contracts: `ANNO001`, `ANNO002`;
- the **generator** (`MetadataRegistryGenerator`) reports what it cannot generate: `ANNO003`, `ANNO004`, `ANNO005`.

Diagnostics only exist in compilations that load the Annotype tooling, which a direct
`<PackageReference Include="Annotype" />` guarantees. Like any diagnostic they can be suppressed
(`#pragma`, `.editorconfig`, `NoWarn`); Annotype is a build-time guard, not a security boundary.

## ANNO001

**Implementation is missing required metadata** (error)

A concrete class implements an interface marked `[RequiresMetadata(typeof(X))]`, directly, through a base
class, or through an inherited interface, but does not declare `[X]` on its own declaration.

```csharp
// error ANNO001: Type 'InvalidPlugin' implements 'Payments.Contracts.IPlugin'
// but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'
public sealed class InvalidPlugin : IPlugin { public void Execute() { } }
```

Rules:

- Concrete classes and record classes are checked. Abstract classes, static classes, structs and record structs
  are not.
- Metadata is **direct-only**: an attribute on a base class does not satisfy the requirement, even if the
  attribute is declared with `Inherited = true`. Re-declare it on each concrete class.
- Attributes are matched by symbol, not by name: a `PluginMetadataAttribute` from another namespace does not count.
- One diagnostic is reported per type and required attribute, even when several contracts require it or the
  type is `partial`. The message lists every contract requiring it.

**Fix:** add the attribute to the class.

## ANNO002

**Invalid metadata contract** (error)

`[RequiresMetadata]` names a type that can never be satisfied:

- `null`;
- a type that does not derive from `System.Attribute`, such as `typeof(string)`;
- an open generic attribute, such as `typeof(TagAttribute<>)`;
- an abstract attribute;
- an attribute whose `[AttributeUsage]` does not include `AttributeTargets.Class`.

The diagnostic is reported on the `[RequiresMetadata(...)]` usage when the contract is in source. When the
contract comes from a referenced assembly, it is reported on each implementing type and names the contract.

**Fix:** reference a concrete, closed attribute type that can be applied to classes.

## ANNO003

**Metadata cannot be reconstructed by generated code** (error)

The generated registry recreates metadata with `new TAttribute(args) { Member = value }` in the
implementation's assembly. That is impossible when something involved is not accessible from a top-level
generated class:

- the implementation type is `private`, `protected` or `file`-local (for example a private nested plugin);
- a `typeof(...)` argument, enum, or array element type is not accessible (for example a private nested type);
- the attribute type, its constructor, or a named property setter or field is not accessible;
- a value has an unsupported shape (for example a multi-dimensional array).

Other valid entries are still generated. The affected entry is never generated partially.

**Fix:** make the types `internal` or `public`.

## ANNO004

**Generic implementation is excluded from the generated registry** (warning)

Generic classes, and classes nested in generic types, are validated by `ANNO001` but not added to the registry,
because a single registry entry cannot represent every closed construction.

**Fix:** derive a non-generic class, annotate it, and use it as the implementation type:

```csharp
[PluginMetadata("cache")]
public sealed class StringCachePlugin : CachePlugin<string> { }
```

## ANNO005

**Multiple metadata values are not supported** (error)

The required attribute is declared with `AllowMultiple = true`. The registry returns exactly one attribute per
implementation and attribute type, so multi-instance attributes are rejected rather than picking one.

**Fix:** declare the required attribute with `AllowMultiple = false`.
