using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class PowerShellSandbox
{
    private readonly ILogger<PowerShellSandbox> _logger;
    private const int TimeoutMs = 30000;

    // Safe cmdlets that extension scripts can use
    private static readonly HashSet<string> SafeCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-Process", "Get-Service", "Get-ChildItem", "Get-Item", "Get-Content",
        "Get-Date", "Get-Location", "Get-ComputerInfo", "Get-NetAdapter",
        "Get-NetIPAddress", "Get-Disk", "Get-Volume", "Get-PnpDevice",
        "Write-Output", "Write-Host", "Out-String", "Out-File",
        "Select-Object", "Where-Object", "ForEach-Object", "Sort-Object",
        "Format-Table", "Format-List", "Measure-Object", "Group-Object",
        "ConvertTo-Json", "ConvertFrom-Json", "ConvertTo-Csv",
        "New-Item", "Copy-Item", "Move-Item", "Rename-Item",
        "Test-Path", "Resolve-Path", "Join-Path", "Split-Path",
        "Start-Process", "Invoke-WebRequest", "Invoke-RestMethod",
        "Set-Clipboard", "Get-Clipboard",
        "Add-Type", "New-Object"
    };

    // Dangerous patterns that should never appear in scripts
    private static readonly string[] DangerousPatterns = new[]
    {
        "Remove-Item -Recurse -Force /",
        "Format-Volume",
        "Clear-Disk",
        "Remove-Partition",
        "Stop-Computer",
        "Restart-Computer",
        "reg delete",
        "Remove-ItemProperty",
        "Set-ExecutionPolicy"
    };

    public PowerShellSandbox(ILogger<PowerShellSandbox> logger)
    {
        _logger = logger;
    }

    public bool ValidateScript(string script)
    {
        foreach (var pattern in DangerousPatterns)
        {
            if (script.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Script rejected: contains dangerous pattern '{Pattern}'", pattern);
                return false;
            }
        }
        return true;
    }

    public string Execute(string script)
    {
        if (!ValidateScript(script))
            return "Error: Script contains potentially dangerous commands and was blocked.";

        // Use ConstrainedLanguage mode for sandboxing
        string wrappedScript = $"$ExecutionContext.SessionState.LanguageMode = 'ConstrainedLanguage'; {script}";
        string escaped = wrappedScript.Replace("\"", "\\\"");

        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return "Error: Failed to start PowerShell process.";

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeoutMs))
        {
            try { process.Kill(); }
            catch (Exception) { /* Process may have already exited */ }
            return "Error: Script execution timed out (30 seconds).";
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            return $"Output:\n{stdout}\n\nErrors:\n{stderr}".Trim();

        return string.IsNullOrWhiteSpace(stdout) ? "Command completed successfully." : stdout.Trim();
    }
}
