namespace Websiter.CLI.Commands;

/// <summary>`websiter clean` — removes build artifacts and state.</summary>
public static class CleanCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var force = args.Contains("--force") || args.Contains("-y");
        var keepState = args.Contains("--keep-state");

        var targets = new List<string>
        {
            Path.Combine(root, "bin"),
            Path.Combine(root, "obj"),
            Path.Combine(root, "publish"),
        };
        if (!keepState)
            targets.Add(Paths.StateDirFor(root));

        var existing = targets.Where(Directory.Exists).ToList();
        if (existing.Count == 0)
        {
            Terminal.Info("Nothing to clean.");
            return Task.FromResult(0);
        }

        if (!force)
        {
            Terminal.Warn("This will delete:");
            foreach (var t in existing) Terminal.Line($"  {t}");
            if (!Terminal.Confirm("Continue?", defaultValue: false))
            {
                Terminal.Info("Aborted.");
                return Task.FromResult(0);
            }
        }

        foreach (var dir in existing)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                Terminal.Success($"Removed {dir}");
            }
            catch (Exception ex)
            {
                Terminal.Error($"Failed to remove {dir}: {ex.Message}");
            }
        }
        return Task.FromResult(0);
    }
}
