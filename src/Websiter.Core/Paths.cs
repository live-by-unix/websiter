namespace Websiter;

/// <summary>
/// Well-known filesystem locations for a Websiter application.
/// Initialized by the runtime with the project root; safe to use before
/// initialization (falls back to the current directory).
/// </summary>
public static class Paths
{
    private static string _root = Directory.GetCurrentDirectory();
    private static string? _stateDir;
    private static string? _logsDir;
    private static string? _pidDir;

    /// <summary>Application content root (project directory).</summary>
    public static string Root => _root;

    /// <summary>Directory containing static web content (<c>wwwroot</c>).</summary>
    public static string WebRoot => Path.Combine(_root, "wwwroot");

    /// <summary>Directory containing worker source files (<c>Workers</c>).</summary>
    public static string WorkersRoot => Path.Combine(_root, "Workers");

    /// <summary>Directory containing HTML components (<c>Components</c>).</summary>
    public static string ComponentsRoot => Path.Combine(_root, "Components");

    /// <summary>Directory containing pages (<c>Pages</c>).</summary>
    public static string PagesRoot => Path.Combine(_root, "Pages");

    /// <summary>Directory containing build output (<c>bin</c>).</summary>
    public static string BinRoot => Path.Combine(_root, "bin");

    /// <summary>Directory containing runtime state (<c>.websiter</c>).</summary>
    public static string StateDir
    {
        get
        {
            _stateDir ??= Path.Combine(_root, ".websiter");
            Directory.CreateDirectory(_stateDir);
            return _stateDir;
        }
    }

    /// <summary>Directory containing runtime logs (<c>.websiter/logs</c>).</summary>
    public static string LogsDir
    {
        get
        {
            _logsDir ??= Path.Combine(StateDir, "logs");
            Directory.CreateDirectory(_logsDir);
            return _logsDir;
        }
    }

    /// <summary>Directory containing PID files (<c>.websiter/pids</c>).</summary>
    public static string PidDir
    {
        get
        {
            _pidDir ??= Path.Combine(StateDir, "pids");
            Directory.CreateDirectory(_pidDir);
            return _pidDir;
        }
    }

    /// <summary>State directory for an explicit root without changing global state.</summary>
    public static string StateDirFor(string root) => Path.Combine(Path.GetFullPath(root), ".websiter");

    /// <summary>PID file for a named process (app or worker).</summary>
    public static string PidFile(string name) => Path.Combine(PidDir, $"{Sanitize(name)}.pid");

    /// <summary>Log file for a named process.</summary>
    public static string LogFile(string name) => Path.Combine(LogsDir, $"{Sanitize(name)}.log");

    /// <summary>Worker status/state file.</summary>
    public static string WorkerStateFile(string name) => Path.Combine(StateDir, $"worker-{Sanitize(name)}.json");

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    internal static void Initialize(string root)
    {
        _root = Path.GetFullPath(root);
        _stateDir = _logsDir = _pidDir = null;
    }
}
