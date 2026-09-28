using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CaPolice.Tests;

/// <summary>
/// Shared helpers for tests that import the built CaPolice module into a real PowerShell 7
/// process and inspect the result. Centralises manifest discovery and pwsh invocation so the
/// individual test classes stay focused on their assertions.
/// </summary>
internal static class PowerShellModuleTestHelper
{
    /// <summary>
    /// Locates the built module manifest, preferring Release but falling back to Debug.
    /// </summary>
    public static string GetModuleManifestPath()
    {
        var repoRoot = GetRepositoryRoot();
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var manifest = Path.Combine(repoRoot, "src", "CaPolice", "bin", configuration, "net8.0", "CaPolice.psd1");
            if (File.Exists(manifest))
                return manifest;
        }

        throw new FileNotFoundException(
            "CaPolice.psd1 not found in bin/Release/net8.0 or bin/Debug/net8.0. Build the CaPolice project first.");
    }

    /// <summary>
    /// Walks up from the test output directory to the repository root (identified by CaPolice.slnx).
    /// </summary>
    public static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CaPolice.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root (CaPolice.slnx).");
    }

    /// <summary>
    /// Runs a PowerShell script in a fresh, profile-free pwsh process and returns the trimmed,
    /// non-empty output lines. Throws when pwsh exits with a non-zero code.
    /// </summary>
    public static async Task<string[]> RunPwshScriptAsync(string script)
    {
        var pwsh = ResolvePwshExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = pwsh,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{pwsh}'.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pwsh exited with code {process.ExitCode}. StdErr: {stdErr}. StdOut: {stdOut}");
        }

        return stdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static string ResolvePwshExecutable()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "pwsh";
}
