using System.Text.Json.Nodes;
using Websiter.Deployment;

namespace Websiter.CLI.Commands;

/// <summary>`websiter deploy`, `domain`, `ssl` — deployment assistance commands.</summary>
public static class DeployCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var target = CliSupport.PositionalArgs(args).FirstOrDefault()?.ToLowerInvariant();

        if (target is null or "local" or "package")
            return await PackageAsync(root).ConfigureAwait(false);

        if (target == "aws")
            return await AwsAsync(root, args).ConfigureAwait(false);

        throw new Program.CliUsageException($"Unknown deploy target '{target}'. Supported: aws, ubuntu, debian, windows (via package + scripts).");
    }

    private static async Task<int> PackageAsync(string root)
    {
        Terminal.Info("Publishing application...");
        var result = await DeploymentPackager.PublishAsync(root).ConfigureAwait(false);
        if (!result.Success)
        {
            Terminal.Error(result.Error ?? "Publish failed.");
            return 1;
        }

        // Generate systemd unit for Linux targets.
        if (!OperatingSystem.IsWindows())
        {
            var appName = CliSupport.AppName(root);
            var unit = SystemdUnitGenerator.Generate(appName, result.OutputDirectory!);
            var unitDir = Path.Combine(result.OutputDirectory!, "systemd");
            Directory.CreateDirectory(unitDir);
            await File.WriteAllTextAsync(Path.Combine(unitDir, SystemdUnitGenerator.UnitFileName(appName)), unit)
                .ConfigureAwait(false);
            Terminal.Success($"Package ready: {result.OutputDirectory}");
            Terminal.Line($"  systemd unit: {unitDir}/{SystemdUnitGenerator.UnitFileName(appName)}");
        }
        else
        {
            Terminal.Success($"Package ready: {result.OutputDirectory}");
        }
        Terminal.Dim("Deploy with: websiter deploy aws  (or copy publish/ to your server)");
        return 0;
    }

    private static async Task<int> AwsAsync(string root, string[] args)
    {
        var cfg = CliSupport.LoadConfigFile(root);
        var deploy = cfg["deploy"]?["aws"];
        var instanceId = deploy?["instanceId"]?.GetValue<string>();
        var region = deploy?["region"]?.GetValue<string>() ?? "us-east-1";
        var sshTarget = deploy?["sshTarget"]?.GetValue<string>();
        var pemFile = deploy?["pemFile"]?.GetValue<string>();
        var appPath = deploy?["remotePath"]?.GetValue<string>() ?? "/opt/websiter";
        var serviceName = deploy?["serviceName"]?.GetValue<string>() ?? CliSupport.AppName(root);

        // Verify AWS CLI.
        var aws = Run("aws", "--version");
        if (aws.ExitCode != 0)
        {
            Terminal.Error("AWS CLI not found. Install it: https://aws.amazon.com/cli/");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            Terminal.Error("deploy:aws:instanceId not configured in websiter.json.");
            Terminal.Dim("Example: {\"deploy\":{\"aws\":{\"instanceId\":\"i-0123...\",\"region\":\"us-east-1\",\"sshTarget\":\"ec2-user@1.2.3.4\",\"pemFile\":\"~/.ssh/key.pem\"}}}");
            return 1;
        }

        // Resolve instance IP.
        var ipResult = Run("aws", $"ec2 describe-instances --instance-ids {instanceId} --region {region} --query \"Reservations[0].Instances[0].PublicIpAddress\" --output text");
        var ip = ipResult.Output.Trim();
        if (ipResult.ExitCode != 0 || string.IsNullOrWhiteSpace(ip) || ip == "None")
        {
            Terminal.Error($"Could not resolve public IP for {instanceId}: {ipResult.Output}");
            return 1;
        }
        Terminal.Info($"Instance {instanceId} → {ip}");

        var target_host = sshTarget ?? $"ec2-user@{ip}";
        var sshOpts = pemFile is null ? "" : $"-i {pemFile} -o StrictHostKeyChecking=accept-new";

        // 1. Publish locally.
        var package = await PackageAsync(root).ConfigureAwait(false);
        if (package != 0) return package;
        var publishDir = Path.Combine(root, "publish");

        // 2. Prepare remote.
        Terminal.Info("Preparing remote host...");
        var prep = Run("ssh", $"{sshOpts} {target_host} \"sudo mkdir -p {appPath} && sudo chown $USER {appPath}\"");
        if (prep.ExitCode != 0) { Terminal.Error($"SSH failed: {prep.Output}"); return 1; }

        // 3. Upload.
        Terminal.Info("Uploading application...");
        var scp = Run("scp", $"{sshOpts} -r \"{publishDir}/.\" \"{target_host}:{appPath}/\"");
        if (scp.ExitCode != 0) { Terminal.Error($"Upload failed: {scp.Output}"); return 1; }

        // 4. Install systemd service + restart.
        var appName = CliSupport.AppName(root);
        var unitService = appName.Replace(' ', '-').ToLowerInvariant();
        var unit = SystemdUnitGenerator.Generate(unitService, appPath);
        var unitPath = $"/etc/systemd/system/{unitService}.service";
        var unitB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(unit));

        var cmds = string.Join(" && ", new[]
        {
            $"echo {unitB64} | base64 -d | sudo tee {unitPath} > /dev/null",
            "sudo systemctl daemon-reload",
            $"sudo systemctl enable {unitService}",
            $"sudo systemctl restart {unitService}",
            $"sleep 2 && sudo systemctl is-active {unitService}",
        });
        var svc = Run("ssh", $"{sshOpts} {target_host} \"{cmds}\"");
        if (svc.ExitCode != 0) { Terminal.Error($"Service install failed: {svc.Output}"); return 1; }

        Terminal.Success($"Deployed to {target_host}:{appPath} (service: {unitService})");
        Terminal.Dim($"Logs: ssh {target_host} 'sudo journalctl -u {unitService} -f'");
        return 0;
    }

    private static (int ExitCode, string Output) Run(string file, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(300000);
            return (p.ExitCode, (stdout + stderr).Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}

/// <summary>`websiter domain add|list|remove` — domain configuration.</summary>
public static class DomainCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var verb = args.FirstOrDefault()?.ToLowerInvariant();
        var domain = CliSupport.PositionalArgs(args.Skip(1).ToArray()).FirstOrDefault();

        var cfg = CliSupport.LoadConfigFile(root);
        var domainsNode = cfg["domains"] as JsonArray ?? new JsonArray();
        cfg["domains"] = domainsNode;

        switch (verb)
        {
            case "add" or "set":
            {
                if (domain is null) throw new Program.CliUsageException("Usage: websiter domain add <domain>");
                if (!System.Text.RegularExpressions.Regex.IsMatch(domain,
                        @"^([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$"))
                    throw new Program.CliUsageException($"Invalid domain '{domain}'.");
                if (!domainsNode.Any(n => n?.GetValue<string>() == domain))
                    domainsNode.Add(domain);
                CliSupport.SaveConfigFile(root, cfg);
                Terminal.Success($"Domain '{domain}' added.");
                Terminal.Dim("Next: point DNS A/AAAA records at your server, then run 'websiter ssl enable'.");
                return Task.FromResult(0);
            }
            case "list":
            {
                if (domainsNode.Count == 0) Terminal.Info("No domains configured.");
                else foreach (var d in domainsNode) Terminal.Line($"  {d?.GetValue<string>()}");
                return Task.FromResult(0);
            }
            case "remove" or "rm":
            {
                if (domain is null) throw new Program.CliUsageException("Usage: websiter domain remove <domain>");
                var node = domainsNode.FirstOrDefault(n => n?.GetValue<string>() == domain);
                if (node is not null) domainsNode.Remove(node);
                CliSupport.SaveConfigFile(root, cfg);
                Terminal.Success($"Domain '{domain}' removed.");
                return Task.FromResult(0);
            }
            default:
                throw new Program.CliUsageException("Usage: websiter domain <add|list|remove> [domain]");
        }
    }
}

/// <summary>`websiter ssl enable` — TLS setup assistance.</summary>
public static class SslCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var root = CliSupport.RequireProjectRoot(args);
        var verb = args.FirstOrDefault()?.ToLowerInvariant() ?? "enable";

        if (verb is not ("enable" or "status"))
            throw new Program.CliUsageException("Usage: websiter ssl <enable|status>");

        var cfg = CliSupport.LoadConfigFile(root);
        var server = cfg["server"] as JsonObject ?? new JsonObject();
        cfg["server"] = server;

        if (verb == "status")
        {
            var useHttps = server["useHttps"]?.GetValue<bool>() ?? false;
            Terminal.Line($"HTTPS: {(useHttps ? "enabled" : "disabled")}");
            Terminal.Line($"Cert:  {server["certificatePath"]?.GetValue<string>() ?? "(not set)"}");
            return Task.FromResult(0);
        }

        var certPath = CliSupport.PositionalArgs(args.Skip(1).ToArray()).FirstOrDefault()
                      ?? server["certificatePath"]?.GetValue<string>();

        if (certPath is null || !File.Exists(certPath))
        {
            Terminal.Warn("No certificate found. Websiter does not issue certificates itself.");
            Terminal.Dim("Obtain a certificate (e.g. certbot --nginx -d example.com), then:");
            Terminal.Dim($"  websiter ssl enable /path/to/cert.pfx [--password secret]");
            Terminal.Line();
            Terminal.Info("For automated TLS termination, put nginx/caddy in front of Websiter");
            Terminal.Dim("and terminate TLS there; or provide a PFX to enable HTTPS in Kestrel.");

            // Check for certbot/caddy availability.
            var certbot = Run("certbot", "--version");
            if (certbot.ExitCode == 0) Terminal.Info($"certbot detected: {certbot.Output.Split('\n')[0]}");
            var caddy = Run("caddy", "version");
            if (caddy.ExitCode == 0) Terminal.Info($"caddy detected: {caddy.Output.Split('\n')[0]}");
            return Task.FromResult(0);
        }

        server["useHttps"] = true;
        server["certificatePath"] = Path.GetFullPath(certPath);
        var pwIdx = Array.IndexOf(args, "--password");
        if (pwIdx >= 0 && pwIdx + 1 < args.Length)
            server["certificatePassword"] = args[pwIdx + 1];
        CliSupport.SaveConfigFile(root, cfg);
        Terminal.Success($"HTTPS enabled with certificate {certPath}");
        Terminal.Dim("Restart the app for changes to take effect.");
        return Task.FromResult(0);
    }

    private static (int ExitCode, string Output) Run(string file, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file, args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o.Trim());
        }
        catch { return (-1, ""); }
    }
}
