using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Annotype.Tooling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Annotype.Tests;

/// <summary>
/// Builds in-memory compilations against the real Annotype runtime and runs the Annotype tooling on them.
/// </summary>
internal static class TestCompilation
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> s_references = new(() =>
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(Metadata).Assembly.Location)
            .Distinct(StringComparer.Ordinal);

        return [.. paths.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    });

    public static ImmutableArray<MetadataReference> References => s_references.Value;

    public static CSharpCompilation Create(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? references = null,
        LanguageVersion languageVersion = LanguageVersion.Latest)
        => Create([source], assemblyName, references, languageVersion);

    public static CSharpCompilation Create(
        string[] sources,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? references = null,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var trees = sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, parseOptions, path: $"Source{i}.cs"));

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            References.AddRange(references ?? []),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: languageVersion >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable));
    }

    /// <summary>
    /// Asserts the compilation has neither errors nor warnings.
    /// </summary>
    public static void AssertClean(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity >= DiagnosticSeverity.Warning).ToList();
        Assert.True(errors.Count == 0, "Compilation diagnostics:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(Compilation compilation)
    {
        var diagnostics = await compilation
            .WithAnalyzers([new MetadataContractAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        return [.. diagnostics
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ThenBy(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)];
    }

    public static GeneratorRun RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new MetadataRegistryGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, TestContext.Current.CancellationToken);
        var result = driver.GetRunResult();

        Assert.All(result.Results, static r => Assert.Null(r.Exception));

        var sources = result.Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.Equal(sources.Count, sources.Select(static s => s.HintName).Distinct(StringComparer.Ordinal).Count());

        return new GeneratorRun(
            driver,
            output,
            diagnostics,
            sources.Count == 0 ? null : sources.Single().SourceText.ToString());
    }

    public static byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, "Emit failed:" + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    public static MetadataReference ToReference(Compilation compilation)
        => MetadataReference.CreateFromImage(Emit(compilation));

    /// <summary>
    /// Loads emitted assemblies in an isolated context. Annotype itself resolves from the default context, so
    /// <see cref="IMetadataRegistry"/> is the same type as in the tests.
    /// </summary>
    public static Assembly Load(Compilation compilation, params Compilation[] dependencies)
    {
        var context = new InMemoryLoadContext();

        foreach (var dependency in dependencies)
        {
            context.Add(dependency.AssemblyName!, Emit(dependency));
        }

        return context.LoadFromStream(new MemoryStream(Emit(compilation)));
    }

    /// <summary>
    /// Returns the generated registry of a loaded assembly.
    /// </summary>
    public static IMetadataRegistry GetRegistry(Assembly assembly, string registryNamespace)
    {
        var type = assembly.GetType(registryNamespace + ".MetadataRegistry", throwOnError: true)!;
        return (IMetadataRegistry)type.GetProperty("Instance")!.GetValue(null)!;
    }

    private sealed class InMemoryLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);

        public InMemoryLoadContext()
            : base(isCollectible: true)
        {
        }

        public void Add(string name, byte[] image) => _images[name] = image;

        protected override Assembly? Load(AssemblyName assemblyName)
            => assemblyName.Name is { } name && _images.TryGetValue(name, out var image)
                ? LoadFromStream(new MemoryStream(image))
                : null;
    }
}

internal sealed record GeneratorRun(
    GeneratorDriver Driver,
    Compilation Output,
    ImmutableArray<Diagnostic> Diagnostics,
    string? Source);
