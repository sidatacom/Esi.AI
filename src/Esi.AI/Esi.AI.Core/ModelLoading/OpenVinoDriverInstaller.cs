using System.Diagnostics;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoDriverInstaller
{
    private static readonly string[][] PackageCandidates =
    [
        ["libze1"],
        ["libze-intel-gpu1", "intel-level-zero-gpu"],
        ["intel-opencl-icd"],
        ["ocl-icd-libopencl1"]
    ];

    public async Task<OpenVinoInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            return new(false, "Automatic driver installation is only supported on Linux.", string.Empty);
        if (!IsSupportedUbuntu(out var distributionMessage))
            return new(false, distributionMessage, string.Empty);

        var updateResult = await RunPkexecAsync(["apt-get", "update"], cancellationToken).ConfigureAwait(false);
        if (updateResult.ExitCode != 0)
            return new(false, "Package list update failed.", updateResult.DiagnosticOutput);

        var packages = PackageCandidates
            .Select(SelectAvailablePackage)
            .ToArray();
        if (packages.Any(package => package is null))
        {
            var missing = string.Join(", ", PackageCandidates
                .Zip(packages)
                .Where(pair => pair.Second is null)
                .Select(pair => string.Join(" or ", pair.First)));
            return new(false, "No compatible Intel GPU runtime package was found.",
                $"Supported Ubuntu detected: {distributionMessage}{Environment.NewLine}Missing package candidate(s): {missing}");
        }

        var arguments = new List<string> { "apt-get", "install", "-y" };
        arguments.AddRange(packages!);
        var packageSelection = $"Selected packages: {string.Join(", ", packages!)}";
        var result = await RunPkexecAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new(true, "Intel Level Zero loader and OpenCL packages were installed. A reboot may be required.", $"{packageSelection}{Environment.NewLine}{result.DiagnosticOutput}")
            : new(false, "Driver package installation failed.", $"{packageSelection}{Environment.NewLine}{result.DiagnosticOutput}");
    }

    public async Task<OpenVinoInstallResult> AddUserToRenderGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            return new(false, "Automatic group setup is only supported on Linux.", string.Empty);

        var commandOutput = new List<string>();
        foreach (var group in new[] { "render", "video" })
        {
            var result = await RunPkexecAsync(["usermod", "-aG", group, Environment.UserName], cancellationToken).ConfigureAwait(false);
            commandOutput.Add(result.DiagnosticOutput);
            if (result.ExitCode != 0)
                return new(false, $"Adding user '{Environment.UserName}' to group '{group}' failed.", string.Join(Environment.NewLine + Environment.NewLine, commandOutput));
        }

        return new(true, "The user is assigned to render and video. Log in again or restart the application for the new groups to apply.", string.Join(Environment.NewLine + Environment.NewLine, commandOutput));
    }

    private static async Task<CommandResult> RunPkexecAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pkexec",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pkexec.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask;
        var error = await errorTask;
        var combinedOutput = string.Join(Environment.NewLine, new[] { output, error }.Where(text => !string.IsNullOrWhiteSpace(text)));
        var command = string.Join(' ', new[] { "pkexec" }.Concat(arguments));
        var diagnosticOutput = $"Command: {command}{Environment.NewLine}Exit code: {process.ExitCode}{Environment.NewLine}" +
            (string.IsNullOrWhiteSpace(combinedOutput)
                ? "The privileged process produced no output. Check the exit code and system authentication log."
                : combinedOutput);
        return new(process.ExitCode, output, error, diagnosticOutput);
    }

    private static string? SelectAvailablePackage(IReadOnlyList<string> candidates) =>
        candidates.FirstOrDefault(IsPackageAvailable);

    private static bool IsPackageAvailable(string package)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "apt-cache",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";
        startInfo.ArgumentList.Add("policy");
        startInfo.ArgumentList.Add(package);
        using var process = Process.Start(startInfo);
        if (process is null)
            return false;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Split('\n')
            .Any(line => line.TrimStart().StartsWith("Candidate:", StringComparison.Ordinal) &&
                !line.Contains("(none)", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedUbuntu(out string message)
    {
        var values = File.ReadAllLines("/etc/os-release")
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1].Trim('"'), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("ID", out var id) || !string.Equals(id, "ubuntu", StringComparison.OrdinalIgnoreCase))
        {
            message = $"Automatic driver installation supports Ubuntu 24.04 and 26.04 only (detected: {id ?? "unknown"}).";
            return false;
        }

        var version = values.GetValueOrDefault("VERSION_ID", "unknown");
        if (version is not ("24.04" or "26.04"))
        {
            message = $"Automatic driver installation supports Ubuntu 24.04 and 26.04 only (detected: {version}).";
            return false;
        }

        message = $"Ubuntu {version} ({values.GetValueOrDefault("VERSION_CODENAME", "unknown")})";
        return true;
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error, string DiagnosticOutput);
}

public sealed record OpenVinoInstallResult(bool Succeeded, string Message, string Output);