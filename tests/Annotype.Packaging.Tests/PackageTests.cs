using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace Annotype.Packaging.Tests;

public class PackageTests(PackageFixture fixture)
{
    private const string ContractsSource = """
        using System;
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
        """;

    private const string ValidPluginSource = """
        using Payments.Contracts;

        namespace Payments.Plugins;

        [PluginMetadata("payments", Priority = 10)]
        public sealed class PaymentPlugin : IPlugin
        {
            public PaymentPlugin() => throw new System.InvalidOperationException("Must not be instantiated.");
            public void Execute() { }
        }
        """;

    private const string InvalidPluginSource = """
        using Payments.Contracts;

        namespace Payments.Plugins;

        public sealed class InvalidPlugin : IPlugin
        {
            public void Execute() { }
        }
        """;

    private const string HostSource = """
        using Annotype;
        using Payments.Contracts;
        using Payments.Plugins;

        Metadata.Register(Annotype.Generated.Payments_Plugins.MetadataRegistry.Instance);
        var metadata = Metadata.Get<PaymentPlugin, PluginMetadataAttribute>();
        System.Console.WriteLine($"{metadata.Name}:{metadata.Priority}");
        """;

    [Fact]
    public void PackageContainsRuntimeAndToolingAtExpectedPaths()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);
        var entries = package.Entries.Select(static e => e.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("lib/netstandard2.0/Annotype.dll", entries);
        Assert.Contains("lib/net8.0/Annotype.dll", entries);
        Assert.Contains("lib/netstandard2.0/Annotype.xml", entries);
        Assert.Contains("analyzers/dotnet/cs/Annotype.Tooling.dll", entries);
        Assert.Contains("README.md", entries);

        // Roslyn is provided by the compiler host and must never be shipped.
        Assert.DoesNotContain(entries, static e => e.Contains("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase));
        Assert.Single(entries, static e => e.StartsWith("analyzers/", StringComparison.Ordinal));

        var nuspecEntry = package.Entries.Single(static e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);
        var dependencies = nuspec.Descendants().Where(static e => e.Name.LocalName == "dependency").Select(static e => (string)e.Attribute("id")!).ToList();

        Assert.Equal(fixture.Version, nuspec.Descendants().Single(static e => e.Name.LocalName == "version").Value);
        Assert.Empty(dependencies);
        Assert.True(File.Exists(Path.ChangeExtension(fixture.PackagePath, ".snupkg")));
    }

    [Fact]
    public async Task SeparatelyBuiltContractPluginAndHostRetrieveMetadata()
    {
        var root = fixture.CreateConsumerRoot("three-assemblies");
        WriteProject(root, "Payments.Contracts", ContractsSource, [AnnotypeReference()]);
        WriteProject(root, "Payments.Plugins", ValidPluginSource, [AnnotypeReference(), ProjectReference("Payments.Contracts")]);
        WriteProject(root, "Payments.Host", HostSource, [AnnotypeReference(), ProjectReference("Payments.Plugins")], exe: true);

        await BuildAsync(root, "Payments.Contracts");
        await BuildAsync(root, "Payments.Plugins");
        await BuildAsync(root, "Payments.Host");

        Assert.True(File.Exists(Path.Combine(root, "Payments.Plugins", "obj", "generated", "Annotype.Tooling", "Annotype.Tooling.MetadataRegistryGenerator", "Annotype.MetadataRegistry.g.cs")));

        var output = await Dotnet.RunAsync(root, ["run", "--project", "Payments.Host", "--no-build"]);
        Assert.Equal("payments:10", output.Trim());
    }

    [Fact]
    public async Task MissingAnnotationFailsBuildWithAnno001()
    {
        var root = fixture.CreateConsumerRoot("invalid-plugin");
        WriteProject(root, "Payments.Contracts", ContractsSource, [AnnotypeReference()]);
        WriteProject(root, "Payments.Plugins", InvalidPluginSource, [AnnotypeReference(), ProjectReference("Payments.Contracts")]);

        await BuildAsync(root, "Payments.Contracts");
        var output = await BuildAsync(root, "Payments.Plugins", expectedExitCode: 1);

        Assert.Contains(
            "error ANNO001: Type 'Payments.Plugins.InvalidPlugin' implements 'Payments.Contracts.IPlugin' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImplementationProjectsNeedTheirOwnAnnotypeReference()
    {
        // Whether analyzers reach a project through a dependency depends on how that dependency references
        // Annotype and on NuGet/SDK behavior, so Annotype never relies on it. Here the contracts package keeps
        // Annotype private: a plugin referencing only the contracts compiles without enforcement or generation,
        // and adding a direct Annotype reference restores both.
        var root = fixture.CreateConsumerRoot("transitive");
        WriteProject(root, "Private.Contracts", ContractsSource, [AnnotypeReference("PrivateAssets=\"all\"")]);
        await PackAsync(root, "Private.Contracts");

        WriteProject(root, "Unenforced.Plugins", InvalidPluginSource, [PackageReference("Private.Contracts")]);
        var unenforced = await BuildAsync(root, "Unenforced.Plugins");
        Assert.DoesNotContain("ANNO001", unenforced, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "Unenforced.Plugins", "obj", "generated", "Annotype.Tooling")));

        WriteProject(root, "Enforced.Plugins", InvalidPluginSource, [PackageReference("Private.Contracts"), AnnotypeReference()]);
        var enforced = await BuildAsync(root, "Enforced.Plugins", expectedExitCode: 1);
        Assert.Contains("error ANNO001", enforced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolingRunsInMinimumSupportedSdk()
    {
        var manifest = Path.Combine(fixture.RepositoryRoot, "tests", "Annotype.Packaging.Tests", "MinimumSdk", "global.json");
        var sdkVersion = JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        var installed = await Dotnet.RunAsync(fixture.WorkDirectory, ["--list-sdks"]);

        if (!installed.Split('\n').Any(line => line.StartsWith(sdkVersion + " ", StringComparison.Ordinal)))
        {
            Assert.Skip($".NET SDK {sdkVersion} is not installed. CI installs it from {manifest}.");
        }

        var root = fixture.CreateConsumerRoot("minimum-sdk", File.ReadAllText(manifest));
        Assert.Equal(sdkVersion, (await Dotnet.RunAsync(root, ["--version"])).Trim());

        WriteProject(root, "Payments.Contracts", ContractsSource, [AnnotypeReference()], targetFramework: "net8.0");
        WriteProject(root, "Payments.Plugins", ValidPluginSource, [AnnotypeReference(), ProjectReference("Payments.Contracts")], targetFramework: "net8.0");
        WriteProject(root, "Payments.Host", HostSource, [AnnotypeReference(), ProjectReference("Payments.Plugins")], exe: true, targetFramework: "net8.0");

        await BuildAsync(root, "Payments.Host");
        var output = await Dotnet.RunAsync(root, ["run", "--project", "Payments.Host", "--no-build"]);
        Assert.Equal("payments:10", output.Trim());

        WriteProject(root, "Payments.Invalid", InvalidPluginSource, [AnnotypeReference(), ProjectReference("Payments.Contracts")], targetFramework: "net8.0");
        var invalid = await BuildAsync(root, "Payments.Invalid", expectedExitCode: 1);
        Assert.Contains("error ANNO001", invalid, StringComparison.Ordinal);
    }

    private Task<string> BuildAsync(string root, string project, int expectedExitCode = 0)
        => Dotnet.RunAsync(root, [
            "build", project,
            "--packages", fixture.PackagesDirectory,
            "--disable-build-servers",
            "-p:NuGetAudit=false",
        ], expectedExitCode);

    private Task PackAsync(string root, string project)
        => Dotnet.RunAsync(root, [
            "pack", project,
            "--output", fixture.FeedDirectory,
            "--packages", fixture.PackagesDirectory,
            "--disable-build-servers",
            "-p:NuGetAudit=false",
            $"-p:Version={fixture.Version}",
        ]);

    private string AnnotypeReference(string attributes = "")
        => $"""<PackageReference Include="Annotype" Version="{fixture.Version}" {attributes} />""";

    private string PackageReference(string id)
        => $"""<PackageReference Include="{id}" Version="{fixture.Version}" />""";

    private static string ProjectReference(string name)
        => $"""<ProjectReference Include="../{name}/{name}.csproj" />""";

    private void WriteProject(string root, string name, string source, string[] items, bool exe = false, string? targetFramework = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;

        File.WriteAllText(Path.Combine(directory, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{targetFramework ?? fixture.ConsumerTargetFramework}</TargetFramework>
                <OutputType>{(exe ? "Exe" : "Library")}</OutputType>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <WarningsAsErrors>$(WarningsAsErrors);CS9057</WarningsAsErrors>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>$(NoWarn);CS1591</NoWarn>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                <CompilerGeneratedFilesOutputPath>obj/generated</CompilerGeneratedFilesOutputPath>
              </PropertyGroup>
              <ItemGroup>
                {string.Join(Environment.NewLine + "    ", items)}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, exe ? "Program.cs" : name + ".cs"), source);
    }
}
