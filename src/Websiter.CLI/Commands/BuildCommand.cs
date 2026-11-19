using Websiter.Deployment;

namespace Websiter.CLI.Commands;

/// <summary>`websiter build` — production build: publish + manifest + assets.</summary>
public static class BuildCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var selfContained = args.Contains("--self-contained");
        string? runtime = null;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--runtime" or "-r") runtime = args[i + 1];

        Terminal.Info($"Building {CliSupport.AppName(root)} ({(selfContained ? "self-contained" : "framework-dependent")})...");
        var result = await DeploymentPackager.PublishAsync(root, selfContained, runtime).ConfigureAwait(false);

        if (!result.Success)
        {
            Terminal.Error(result.Error ?? "Build failed.");
            return 1;
        }

        Terminal.Success($"Build succeeded in {result.Duration.TotalSeconds:0.0}s");
        Terminal.Line($"  Output:    {result.OutputDirectory}");
        Terminal.Line($"  Assembly:  {result.AssemblyPath}");
        Terminal.Line("  Manifest:  .websiter/app.json");
        return 0;
    }
}
