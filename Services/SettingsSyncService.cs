using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class SettingsSyncService
{
    private readonly ILogger<SettingsSyncService> _logger;

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass");

    public SettingsSyncService(ILogger<SettingsSyncService> logger)
    {
        _logger = logger;
    }

    public void Export(string zipPath)
    {
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            // Settings
            AddFileToZip(zip, Path.Combine(AppDataPath, "settings.json"), "settings.json");

            // Shortcuts
            AddFileToZip(zip, Path.Combine(AppDataPath, "shortcuts.json"), "shortcuts.json");

            // Snippets
            AddFileToZip(zip, Path.Combine(AppDataPath, "snippets.json"), "snippets.json");

            // Extensions
            string extDir = Path.Combine(AppDataPath, "Extensions");
            if (Directory.Exists(extDir))
            {
                foreach (var file in Directory.GetFiles(extDir, "*.json"))
                    AddFileToZip(zip, file, $"Extensions/{Path.GetFileName(file)}");
            }

            // Frecency data
            AddFileToZip(zip, Path.Combine(AppDataPath, "frecency.json"), "frecency.json");

            _logger.LogInformation("Settings exported to {ZipPath}", zipPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export settings");
            throw;
        }
    }

    public void Import(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            foreach (var entry in zip.Entries)
            {
                string destPath = Path.Combine(AppDataPath, entry.FullName);
                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                // Skip API key entries (they stay in Credential Manager)
                if (entry.Name.EndsWith(".key")) continue;

                entry.ExtractToFile(destPath, overwrite: true);
            }

            _logger.LogInformation("Settings imported from {ZipPath}", zipPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import settings");
            throw;
        }
    }

    private static void AddFileToZip(ZipArchive zip, string filePath, string entryName)
    {
        if (File.Exists(filePath))
            zip.CreateEntryFromFile(filePath, entryName);
    }
}
