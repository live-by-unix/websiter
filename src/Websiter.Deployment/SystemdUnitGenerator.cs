namespace Websiter.Deployment;

/// <summary>Generates systemd unit files for production deployments.</summary>
public static class SystemdUnitGenerator
{
    /// <summary>
    /// Generates a systemd service unit for a Websiter app.
    /// <paramref name="appPath"/> is the directory containing the published app;
    /// <paramref name="appName"/> is used for the unit/service name.
    /// </summary>
    public static string Generate(string appName, string appPath, string? user = null,
        string environment = "Production", int restartSec = 5)
    {
        var execStart = OperatingSystem.IsWindows()
            ? Path.Combine(appPath, appName + ".exe")
            : $"/usr/bin/dotnet {Path.Combine(appPath, appName + ".dll")}";

        if (!OperatingSystem.IsWindows() && !File.Exists(Path.Combine(appPath, appName + ".dll")))
        {
            // Apphost exists?
            var host = Path.Combine(appPath, appName);
            if (File.Exists(host)) execStart = host;
        }

        var userLine = user is null ? "" : $"User={user}\n";
        return $"""
        [Unit]
        Description=Websiter application '{appName}'
        After=network.target

        [Service]
        Type=notify
        {userLine}WorkingDirectory={appPath}
        ExecStart={execStart}
        Restart=on-failure
        RestartSec={restartSec}
        Environment=WEBSITER_ENV={environment}
        Environment=DOTNET_ENVIRONMENT={environment}
        StandardOutput=append:{Path.Combine(appPath, "logs", "app.log")}
        StandardError=append:{Path.Combine(appPath, "logs", "app-error.log")}

        [Install]
        WantedBy=multi-user.target
        """;
    }

    /// <summary>Writes the unit file content to a string path-safe location.</summary>
    public static string UnitFileName(string appName) => $"{appName}.service";
}
