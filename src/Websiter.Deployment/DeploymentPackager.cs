using System.Diagnostics;
using System.Text.Json;

namespace Websiter.Deployment;

/// <summary>Writes the application manifest consumed by the runtime and CLI.</summary>
public static class AppManifest
{
    /// <summary>Path of the manifest file for a project root.</summary>
    public static string PathFor(string root) => System.IO.Path.Combine(Websiter.Paths.StateDirFor(root), "app.json");

    /// <summary>Writes <c>.websiter/app.json</c> describing the built application.</summary>
    public static void Write(string root, string assemblyPath, string configuration)
    {
        var dir = Websiter.Paths.StateDirFor(root);
        Directory.CreateDirectory(dir);
        var manifest = new
        {
            assembly = Path.GetFullPath(assemblyPath),
            configuration,
            builtUtc = DateTimeOffset.UtcNow,
            framework = "Websiter",
        };
        File.WriteAllText(PathFor(root), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Result of a build operation.</summary>
public sealed class BuildResult
{
    public required bool Success { get; init; }
    public string? AssemblyPath { get; init; }
    public string? OutputDirectory { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Production build: runs <c>dotnet publish</c> in Release configuration and
/// writes the application manifest plus the frontend asset manifest.
/// </summary>
public static class DeploymentPackager
{
    /// <summary>Publishes the project to <c>publish/</c> (framework-dependent by default).</summary>
    public static async Task<BuildResult> PublishAsync(string root, bool selfContained = false, string? runtime = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var outputDir = System.IO.Path.Combine(root, "publish");
        var configuration = "Release";

        var csproj = Directory.EnumerateFiles(root, "*.csproj").FirstOrDefault();
        if (csproj is null)
            return new BuildResult { Success = false, Error = "No .csproj found in project root.", Duration = sw.Elapsed };

        var args = $"publish \"{csproj}\" -c {configuration} -o \"{outputDir}\"";
        if (selfContained) args += " --self-contained true";
        if (runtime is not null) args += $" -r {runtime}";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        sw.Stop();
        if (process.ExitCode != 0)
        {
            return new BuildResult
            {
                Success = false,
                Error = $"dotnet publish failed (exit {process.ExitCode}):\n{stderr}\n{stdout}",
                Duration = sw.Elapsed,
            };
        }

        var assemblyName = System.IO.Path.GetFileNameWithoutExtension(csproj);
        var dll = System.IO.Path.Combine(outputDir, assemblyName + ".dll");
        var assembly = File.Exists(dll) ? dll : Directory.EnumerateFiles(outputDir, "*.dll")
            .FirstOrDefault(f => !System.IO.Path.GetFileName(f).StartsWith("Websiter.") && !System.IO.Path.GetFileName(f).StartsWith("Microsoft."));

        if (assembly is not null)
            AppManifest.Write(root, assembly, configuration);

        return new BuildResult
        {
            Success = true,
            AssemblyPath = assembly,
            OutputDirectory = outputDir,
            Duration = sw.Elapsed,
        };
    }
}
