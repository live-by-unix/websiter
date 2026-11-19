namespace Websiter.Native;

/// <summary>Well-known filesystem locations for a Websiter application.</summary>
public static class ProjectPaths
{
    /// <summary>State directory for a project root without mutating global state.</summary>
    public static string StateDir(string root) => Path.Combine(Path.GetFullPath(root), ".websiter");

    /// <summary>Whether a directory looks like a Websiter project.</summary>
    public static bool IsWebsiterProject(string dir)
        => File.Exists(Path.Combine(dir, "websiter.json"))
           || Directory.EnumerateFiles(dir, "*.csproj").Any()
           || Directory.Exists(Path.Combine(dir, "Workers"));

    /// <summary>Finds the project root walking upward from a start directory.</summary>
    public static string FindRoot(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            if (IsWebsiterProject(dir.FullName)) return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(startDir);
    }
}
