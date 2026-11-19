using System.Reflection;
using Websiter.CLI.Commands;

namespace Websiter.CLI;

/// <summary>
/// websiter — the Websiter command-line interface.
/// Exit codes: 0 success, 1 general error, 2 usage error.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help.Print(args.Length > 0 ? args[1..] : Array.Empty<string>());
            return 0;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine($"websiter {GetVersion()}");
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "new" => await NewCommand.RunAsync(args[1..]),
                "dev" => await DevCommand.RunAsync(args[1..]),
                "build" => await BuildCommand.RunAsync(args[1..]),
                "start" => await StartCommand.RunAsync(args[1..]),
                "stop" => await StopCommand.RunAsync(args[1..]),
                "restart" => await RestartCommand.RunAsync(args[1..]),
                "workers" => await WorkersCommand.RunAsync(args[1..]),
                "routes" => await RoutesCommand.RunAsync(args[1..]),
                "logs" => await LogsCommand.RunAsync(args[1..]),
                "doctor" => await DoctorCommand.RunAsync(args[1..]),
                "config" => await ConfigCommand.RunAsync(args[1..]),
                "clean" => await CleanCommand.RunAsync(args[1..]),
                "deploy" => await DeployCommand.RunAsync(args[1..]),
                "domain" => await DomainCommand.RunAsync(args[1..]),
                "ssl" => await SslCommand.RunAsync(args[1..]),
                _ => Unknown(args[0]),
            };
        }
        catch (CliUsageException ex)
        {
            Terminal.Error(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Terminal.Error($"{ex.GetType().Name}: {ex.Message}");
            if (Terminal.Verbose) Terminal.Error(ex.ToString());
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Terminal.Error($"Unknown command '{command}'.");
        Terminal.Write("Run 'websiter --help' for usage.");
        return 2;
    }

    internal static string GetVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var v = info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
        return v.Split('+')[0];
    }

    /// <summary>Thrown for command-line usage errors (exit code 2).</summary>
    public sealed class CliUsageException : Exception
    {
        public CliUsageException(string message) : base(message) { }
    }
}
