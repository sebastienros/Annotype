# AGENTS.md

Guidance for AI agents working in this repository. This is the single source of truth: `CLAUDE.md` and
`.github/copilot-instructions.md` only point here. Record new guidance in this file.

Annotype moves type-level invariants from instances to types: `[RequiresMetadata]` contracts are enforced by an
analyzer, and a source generator emits a reflection-free registry read through `Annotype.Metadata`. Read
`docs/architecture.md` before changing the tooling.

## Layout

- `src/Annotype`: runtime API (`netstandard2.0`, `net8.0`). Packs the tooling into `analyzers/dotnet/cs`.
- `src/Annotype.Tooling`: analyzer and incremental generator (`netstandard2.0`, Roslyn 4.8). Not packed on its own.
- `samples/Payments.*`: contracts, plugins and host as three assemblies.
- `tests/Annotype.Analyzers.Tests`, `tests/Annotype.Generators.Tests`: in-memory Roslyn compilations.
- `tests/Annotype.Runtime.Tests`: runtime API, with the generator running on the test project and the sample plugins.
- `tests/Annotype.Packaging.Tests`: packs from a copy of `src/` and builds isolated consumers with the dotnet CLI.

## Build and test

The SDK is pinned by `global.json` (10.0.100, `rollForward: latestMajor`). Tests use Microsoft.Testing.Platform
with xunit v3.

```bash
dotnet build
dotnet test
dotnet test tests/Annotype.Generators.Tests -- --filter-method "*RoundTrip*"
```

`ANNOTYPE_KEEP_TEST_FILES=1` keeps the packaging tests' temporary feed and consumer projects for inspection.

## Rules

- Never add runtime reflection, assembly scanning or `Activator` to the runtime or to generated code.
- Generated code must stay valid C# 7.3, use `global::` names, and compile without warnings with nullable enabled.
- Compare symbols with `SymbolEqualityComparer.Default`, never by name.
- Unsupported shapes must produce a diagnostic, never a silently skipped or partial entry.
- Do not raise `Microsoft.CodeAnalysis.CSharp` without updating the minimum SDK test and the README.
- New diagnostics need a descriptor in `DiagnosticDescriptors`, an entry in `AnalyzerReleases.Unshipped.md` and a
  section in `docs/diagnostics.md`.
