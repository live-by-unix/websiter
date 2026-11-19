using System.Text.RegularExpressions;

namespace Websiter.CLI.Commands;

/// <summary>`websiter new` — scaffolds a project from a template.</summary>
public static class NewCommand
{
    private static readonly string[] Templates = { "basic", "fullstack", "api", "distributed" };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) throw new Program.CliUsageException("Missing project name. Usage: websiter new <name> [--template <t>]");

        var name = args[0];
        if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_\-]{0,63}$"))
            throw new Program.CliUsageException($"Invalid project name '{name}'. Use letters, digits, '-' and '_', starting with a letter.");

        string? template = null;
        var dir = Directory.GetCurrentDirectory();
        var nonInteractive = args.Contains("--non-interactive") || args.Contains("-y");
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--template" or "-t" when i + 1 < args.Length: template = args[++i]; break;
                case "--dir" or "-o" when i + 1 < args.Length: dir = Path.GetFullPath(args[++i]); break;
                case "--non-interactive" or "-y": break;
                case "--help" or "-h": PrintHelp(); return 0;
                default: throw new Program.CliUsageException($"Unknown option '{args[i]}'.");
            }
        }

        template ??= await ResolveTemplateAsync(nonInteractive).ConfigureAwait(false);
        if (!Templates.Contains(template, StringComparer.OrdinalIgnoreCase))
            throw new Program.CliUsageException($"Unknown template '{template}'. Available: {string.Join(", ", Templates)}");
        template = template.ToLowerInvariant();

        var target = Path.Combine(dir, name);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new Program.CliUsageException($"Directory '{target}' already exists and is not empty.");

        var templatesRoot = FindTemplatesRoot()
            ?? throw new InvalidOperationException(
                "Websiter templates not found. Set WEBSITER_TEMPLATES to the templates directory.");

        var templateDir = Path.Combine(templatesRoot, template);
        if (!Directory.Exists(templateDir))
            throw new InvalidOperationException($"Template '{template}' not found at {templateDir}.");

        Directory.CreateDirectory(target);
        string Sanitize(string segment) => segment
            .Replace("__PROJECT__", name)
            .Replace("__PROJECTLOWER__", name.ToLowerInvariant());

        foreach (var file in Directory.EnumerateFiles(templateDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(templateDir, file);
            // Placeholders may appear in file/dir names (e.g. __PROJECT__.csproj).
            var sanitized = string.Join(Path.DirectorySeparatorChar.ToString(),
                relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Select(Sanitize));
            var dest = Path.Combine(target, sanitized);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        // Apply placeholders to the contents of all text files.
        var frameworkSrc = Path.GetFullPath(Path.Combine(templatesRoot, "..", "src"));
        // Resolve symlinks so generated projects reference the real framework path.
        var srcDir = new DirectoryInfo(frameworkSrc);
        if (srcDir.Exists && srcDir.ResolveLinkTarget(returnFinalTarget: true) is { } resolved)
            frameworkSrc = resolved.FullName;
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".csproj", ".json", ".html", ".htm", ".css", ".js", ".md", ".txt", ".xml", ".yml", ".yaml", ".config" };
        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            if (!textExtensions.Contains(Path.GetExtension(file))) continue;
            var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            if (!text.Contains("__")) continue;
            text = text.Replace("__PROJECT__", name)
                       .Replace("__PROJECTLOWER__", name.ToLowerInvariant())
                       .Replace("__WEBSITER_SRC__", frameworkSrc);
            await File.WriteAllTextAsync(file, text).ConfigureAwait(false);
        }

        Terminal.Success($"Created '{name}' from the '{template}' template at {target}");
        Terminal.Dim("Next steps:");
        Terminal.Line($"  cd {name}");
        Terminal.Line("  websiter dev      # build, run and watch for changes");
        return 0;
    }

    private static async Task<string> ResolveTemplateAsync(bool nonInteractive)
    {
        if (nonInteractive || !Console.IsInputRedirected)
        {
            if (!nonInteractive)
            {
                Terminal.Line("Available templates:");
                for (var i = 0; i < Templates.Length; i++)
                    Terminal.Line($"  {i + 1}. {Templates[i]}");
                var choice = Terminal.Prompt("Choose a template", "1");
                if (int.TryParse(choice, out var n) && n >= 1 && n <= Templates.Length)
                    return Templates[n - 1];
            }
            return "basic";
        }
        return "basic";
    }

    /// <summary>Locates the templates directory: env override, beside the CLI, or repo ancestors.</summary>
    public static string? FindTemplatesRoot()
    {
        var env = Environment.GetEnvironmentVariable("WEBSITER_TEMPLATES");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "templates"),
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "templates"));
            dir = dir.Parent;
        }
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (cwd is not null)
        {
            candidates.Add(Path.Combine(cwd.FullName, "templates"));
            cwd = cwd.Parent;
        }

        return candidates.FirstOrDefault(c =>
            Directory.Exists(c) && File.Exists(Path.Combine(c, "basic", "websiter.json")));
    }

    private static void PrintHelp()
    {
        Terminal.Line("websiter new <name> [--template basic|fullstack|api|distributed] [--dir <path>] [--non-interactive]");
    }
}
