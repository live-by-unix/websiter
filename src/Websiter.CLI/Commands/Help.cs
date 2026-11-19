namespace Websiter.CLI.Commands;

/// <summary>Prints CLI help.</summary>
public static class Help
{
    public static void Print(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "new": Terminal.Line("websiter new <name> [--template basic|fullstack|api|distributed] [--dir <path>] [--non-interactive]"); return;
                case "dev": Terminal.Line("websiter dev [--no-watch] [--root <path>]"); return;
                case "build": Terminal.Line("websiter build [--self-contained] [--runtime <rid>] [--root <path>]"); return;
                case "start": Terminal.Line("websiter start [--root <path>]"); return;
                case "stop": Terminal.Line("websiter stop [--root <path>]"); return;
                case "restart": Terminal.Line("websiter restart [--root <path>]"); return;
                case "workers": Terminal.Line("websiter workers <list|start|stop|restart|logs> [name] [-f]"); return;
                case "deploy": Terminal.Line("websiter deploy [aws|package] [--root <path>]"); return;
            }
        }

        Terminal.Bold("websiter — Build without boundaries.");
        Terminal.Line();
        Terminal.Line("Usage: websiter <command> [options]");
        Terminal.Line();
        Terminal.Line("Project:");
        Terminal.Line("  new <name> [--template <t>]   Create a new Websiter project");
        Terminal.Line("  dev [--no-watch]             Build, run and watch for changes");
        Terminal.Line("  build [--self-contained]     Production build to publish/");
        Terminal.Line("  start | stop | restart       Manage the running application");
        Terminal.Line("  clean [--keep-state] [-y]    Remove build artifacts");
        Terminal.Line();
        Terminal.Line("Workers:");
        Terminal.Line("  workers list                  List workers and status");
        Terminal.Line("  workers start <name>          Start a worker");
        Terminal.Line("  workers stop <name>           Stop a worker");
        Terminal.Line("  workers restart <name>        Restart a worker");
        Terminal.Line("  workers logs <name> [-f]      Show worker logs");
        Terminal.Line();
        Terminal.Line("Diagnostics:");
        Terminal.Line("  routes                        List registered HTTP routes");
        Terminal.Line("  logs [-f] [name]              Show application logs");
        Terminal.Line("  doctor                        Check environment and project health");
        Terminal.Line("  config                        Show configuration (secrets masked)");
        Terminal.Line();
        Terminal.Line("Deployment:");
        Terminal.Line("  deploy [aws]                  Package or deploy to AWS EC2");
        Terminal.Line("  domain add|list|remove <d>    Manage domains");
        Terminal.Line("  ssl enable|status [cert.pfx]  TLS certificate setup");
        Terminal.Line();
        Terminal.Line("Other:");
        Terminal.Line("  --version                     Print version");
        Terminal.Line("  --help                         Show this help");
        Terminal.Line();
        Terminal.Dim("Global options: --root <path>  WEBSITER_TEMPLATES env for template dir");
    }
}
