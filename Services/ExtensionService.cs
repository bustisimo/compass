using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class ExtensionService : IExtensionService
{
    public string ExtensionsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "Extensions");

    private readonly ILogger<ExtensionService> _logger;

    public ExtensionService(ILogger<ExtensionService> logger)
    {
        _logger = logger;
    }

    public void EnsureExtensionsFolderExists()
    {
        if (!Directory.Exists(ExtensionsPath))
            Directory.CreateDirectory(ExtensionsPath);
    }

    public List<CompassExtension> LoadExtensions()
    {
        var result = new List<CompassExtension>();
        if (!Directory.Exists(ExtensionsPath)) return result;

        foreach (var file in Directory.GetFiles(ExtensionsPath, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var ext = JsonSerializer.Deserialize<CompassExtension>(json);
                if (ext != null) result.Add(ext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load extension from {File}", file);
            }
        }
        return result;
    }

    public void SaveExtension(CompassExtension ext)
    {
        EnsureExtensionsFolderExists();
        string json = JsonSerializer.Serialize(ext, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ExtensionsPath, $"{ext.TriggerName}.json"), json);
    }

    public void DeleteExtension(string triggerName)
    {
        string path = Path.Combine(ExtensionsPath, $"{triggerName}.json");
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to delete extension {TriggerName}", triggerName); }
        }
    }

    public string ExecuteExtension(CompassExtension ext)
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"compass_{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(tempFile, ext.PowerShellScript);

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"")
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
            process.WaitForExit(60000); // 60 second timeout

            if (!string.IsNullOrWhiteSpace(stderr))
                return $"Output:\n{stdout}\n\nErrors:\n{stderr}".Trim();

            return string.IsNullOrWhiteSpace(stdout) ? "Command completed successfully." : stdout.Trim();
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch { /* best-effort cleanup */ }
        }
    }
}
