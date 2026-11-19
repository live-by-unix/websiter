using System.Reflection;
using System.Text.RegularExpressions;

namespace Websiter.Workers;

/// <summary>A discovered worker definition.</summary>
public sealed class WorkerDefinition
{
    public required string Name { get; init; }
    public string? TypeName { get; init; }
    public string? SourceFile { get; init; }
    public List<string> DependsOn { get; init; } = new();
}

/// <summary>Discovers <see cref="Worker"/> implementations in assemblies and source files.</summary>
public static class WorkerDiscovery
{
    /// <summary>Finds all concrete <see cref="Worker"/> subclasses in an assembly.</summary>
    public static IReadOnlyList<WorkerDefinition> DiscoverInAssembly(Assembly assembly)
    {
        var results = new List<WorkerDefinition>();
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(Worker).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            var name = type.Name;
            List<string> deps = new();
            try
            {
                // Read DependsOn via a lightweight instance-free check is not possible;
                // dependencies are resolved at runtime by the supervisor from instances
                // when the assembly is loaded in-process. For discovery we expose the
                // virtual property through a temporary instance only when cheap.
            }
            catch { }

            results.Add(new WorkerDefinition { Name = name, TypeName = type.FullName });
        }
        return results;
    }

    private static readonly Regex _classPattern = new(
        @"class\s+(?<name>\w+)\s*:\s*(?:[\w.]+\s*,\s*)*(?:[\w.]*\.)?Worker\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans <c>Workers/*.cs</c> for classes deriving from <see cref="Worker"/>.
    /// Used by the CLI before a build exists.
    /// </summary>
    public static IReadOnlyList<WorkerDefinition> DiscoverFromFiles(string workersDir)
    {
        var results = new List<WorkerDefinition>();
        if (!Directory.Exists(workersDir)) return results;

        foreach (var file in Directory.EnumerateFiles(workersDir, "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(file); } catch { continue; }
            foreach (Match match in _classPattern.Matches(text))
            {
                results.Add(new WorkerDefinition
                {
                    Name = match.Groups["name"].Value,
                    SourceFile = file,
                });
            }
        }
        return results.DistinctBy(d => d.Name).ToList();
    }

    /// <summary>Instantiates a worker to read its declared dependencies (requires the assembly loaded).</summary>
    public static IReadOnlyList<string> GetDependencies(Worker worker) => worker.DependsOn;
}
