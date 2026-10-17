using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

[assembly: AssemblyFixture(typeof(Annotype.Packaging.Tests.PackageFixture))]

namespace Annotype.Packaging.Tests;

/// <summary>
/// Packs Annotype once per test run from a copy of the sources, so the repository's own build outputs are
/// never touched, into a local feed used by the consumer projects.
/// </summary>
public sealed class PackageFixture : IAsyncLifetime
{
    public PackageFixture()
    {
        RepositoryRoot = FindRepositoryRoot();
        WorkDirectory = Path.Combine(Path.GetTempPath(), "annotype-packaging-tests", Guid.NewGuid().ToString("N")[..12]);
        FeedDirectory = Path.Combine(WorkDirectory, "feed");
        PackagesDirectory = Path.Combine(WorkDirectory, "packages");
        Version = "0.0.0-test." + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string RepositoryRoot { get; }

    public string WorkDirectory { get; }

    public string FeedDirectory { get; }

    /// <summary>
    /// An isolated NuGet packages folder so that test versions never land in the global cache.
    /// </summary>
    public string PackagesDirectory { get; }

    public string Version { get; }

    public string PackagePath => Path.Combine(FeedDirectory, $"Annotype.{Version}.nupkg");

    public string ConsumerTargetFramework { get; } = $"net{Environment.Version.Major}.0";

    public async ValueTask InitializeAsync()
    {
        var source = Path.Combine(WorkDirectory, "source");

        foreach (var file in new[] { "Directory.Build.props", "Directory.Packages.props", "NuGet.config", "global.json", "README.md", "LICENSE" })
        {
            CopyFile(Path.Combine(RepositoryRoot, file), Path.Combine(source, file));
        }

        CopyFile(Path.Combine(RepositoryRoot, "assets", "icon.png"), Path.Combine(source, "assets", "icon.png"));
        CopyDirectory(Path.Combine(RepositoryRoot, "src", "Annotype"), Path.Combine(source, "src", "Annotype"));
        CopyDirectory(Path.Combine(RepositoryRoot, "src", "Annotype.Tooling"), Path.Combine(source, "src", "Annotype.Tooling"));

        await Dotnet.RunAsync(source, [
            "pack", "src/Annotype/Annotype.csproj",
            "--configuration", "Release",
            "--output", FeedDirectory,
            $"-p:Version={Version}",
            // The copy is not a git repository.
            "-p:EnableSourceLink=false",
            "-p:EnableSourceControlManagerQueries=false",
        ]);

        Assert.True(File.Exists(PackagePath), $"Package '{PackagePath}' was not produced.");
    }

    public ValueTask DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable("ANNOTYPE_KEEP_TEST_FILES") == "1")
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            Directory.Delete(WorkDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS cleans temporary files.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Creates an empty directory for a consumer scenario, configured to restore Annotype from the local feed.
    /// </summary>
    public string CreateConsumerRoot(string name, string? globalJson = null)
    {
        var root = Path.Combine(WorkDirectory, "consumers", name);
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{FeedDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        // Stop MSBuild from importing anything above the consumer.
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), "<Project />");

        if (globalJson is null)
        {
            File.Copy(Path.Combine(RepositoryRoot, "global.json"), Path.Combine(root, "global.json"));
        }
        else
        {
            File.WriteAllText(Path.Combine(root, "global.json"), globalJson);
        }

        return root;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Annotype.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root (Annotype.sln).");
    }

    private static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];

            if (first is "bin" or "obj")
            {
                continue;
            }

            CopyFile(file, Path.Combine(to, relative));
        }
    }
}

internal static class Dotnet
{
    /// <summary>
    /// Runs the dotnet CLI and returns its combined output. Throws when the exit code differs from the expected one.
    /// </summary>
    public static async Task<string> RunAsync(string workingDirectory, IEnumerable<string> arguments, int expectedExitCode = 0, IDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // The test host may run under MSBuild ('dotnet test'); do not leak its settings into child builds.
        foreach (var key in info.Environment.Keys.ToList())
        {
            if (key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) || key.Equals("DOTNET_HOST_PATH", StringComparison.OrdinalIgnoreCase))
            {
                info.Environment.Remove(key);
            }
        }

        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        info.Environment["UseSharedCompilation"] = "false";

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info)!;
        var output = new StringBuilder();
        var stdout = PumpAsync(process.StandardOutput, output);
        var stderr = PumpAsync(process.StandardError, output);

        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'dotnet {string.Join(' ', info.ArgumentList)}' timed out.{Environment.NewLine}{output}");
        }

        await Task.WhenAll(stdout, stderr);
        var text = output.ToString();

        if (process.ExitCode != expectedExitCode)
        {
            throw new InvalidOperationException(
                $"'dotnet {string.Join(' ', info.ArgumentList)}' in '{workingDirectory}' exited with {process.ExitCode} (expected {expectedExitCode}).{Environment.NewLine}{text}");
        }

        return text;
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder output)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (output)
            {
                output.AppendLine(line);
            }
        }
    }
}
