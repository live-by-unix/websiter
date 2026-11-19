namespace Websiter.CLI;

/// <summary>Colorized terminal output with graceful degradation.</summary>
public static class Terminal
{
    private static readonly bool _color = DetectColor();

    public static bool Verbose { get; set; }

    private static bool DetectColor()
    {
        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null) return false;
        if (Environment.GetEnvironmentVariable("TERM") == "dumb") return false;
        if (Environment.GetEnvironmentVariable("CI") is not null
            && Environment.GetEnvironmentVariable("CI") is "true" or "1")
            return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null ? false : false;
        try { return !Console.IsOutputRedirected; }
        catch { return false; }
    }

    public static bool Colors => _color;

    public static void EnableVerbose() => Verbose = true;

    public static void Write(string message = "")
    {
        if (_color)
            Console.Write(message);
        else
            Console.Write(StripAnsi(message));
    }

    public static void Line(string message = "") => Write(message + "\n");

    public static void Info(string message) => Line($"{C("\u001b[36m")}info{C("\u001b[0m")}  {message}");
    public static void Success(string message) => Line($"{C("\u001b[32m")} ok {C("\u001b[0m")}  {message}");
    public static void Warn(string message) => Line($"{C("\u001b[33m")}warn{C("\u001b[0m")}  {message}");
    public static void Error(string message) => Line($"{C("\u001b[31m")}fail{C("\u001b[0m")}  {message}");
    public static void Dim(string message) => Line($"{C("\u001b[90m")}{message}{C("\u001b[0m")}");
    public static void Bold(string message) => Line($"{C("\u001b[1m")}{message}{C("\u001b[0m")}");

    /// <summary>Shows a spinner line while an async operation runs (TTY only).</summary>
    public static async Task WithSpinnerAsync(string message, Func<Task> action)
    {
        if (!_color || Console.IsOutputRedirected)
        {
            Line($"... {message}");
            await action().ConfigureAwait(false);
            return;
        }

        var frames = new[] { "|", "/", "-", "\\" };
        var i = 0;
        using var cts = new CancellationTokenSource();
        var spinner = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                Console.Write($"\r{C("\u001b[36m")}{frames[i++ % frames.Length]}{C("\u001b[0m")} {message}   ");
                try { await Task.Delay(80, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        });
        try { await action().ConfigureAwait(false); }
        finally
        {
            cts.Cancel();
            try { await spinner.ConfigureAwait(false); } catch { }
            Console.Write("\r" + new string(' ', message.Length + 8) + "\r");
        }
    }

    /// <summary>Reads a line from stdin; returns null on EOF. Non-interactive safe.</summary>
    public static string? Prompt(string question, string? defaultValue = null)
    {
        var suffix = defaultValue is null ? "" : $" [{defaultValue}]";
        Console.Out.Write($"{question}{suffix}: ");
        var answer = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(answer)) return defaultValue;
        return answer.Trim();
    }

    /// <summary>Yes/no prompt; returns default on empty input.</summary>
    public static bool Confirm(string question, bool defaultValue = false)
    {
        var suffix = defaultValue ? " [Y/n]" : " [y/N]";
        Console.Out.Write($"{question}{suffix}: ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        return answer is "y" or "yes";
    }

    private static string C(string ansi) => _color ? ansi : "";

    private static string StripAnsi(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        var inEsc = false;
        foreach (var ch in s)
        {
            if (ch == '\u001b') { inEsc = true; continue; }
            if (inEsc)
            {
                if ((ch >= '@' && ch <= '~')) inEsc = false;
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
