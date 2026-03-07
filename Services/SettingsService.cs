using System.IO;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class SettingsService : ISettingsService
{
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass");
    private static readonly string SettingsFile = Path.Combine(DataPath, "settings.json");
    private static readonly string ShortcutsFile = Path.Combine(DataPath, "shortcuts.json");

    private readonly ILogger<SettingsService> _logger;
    private readonly ICredentialService _credentialService;

    public SettingsService(ILogger<SettingsService> logger, ICredentialService credentialService)
    {
        _logger = logger;
        _credentialService = credentialService;
    }

    public void EnsureDirectoryExists() => Directory.CreateDirectory(DataPath);

    public AppSettings LoadSettings()
    {
        EnsureDirectoryExists();
        MigrateIfNeeded();

        if (File.Exists(SettingsFile))
        {
            try
            {
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (settings.AvailableModels == null || !settings.AvailableModels.Any())
                    settings.AvailableModels = new List<string> { "gemini-1.5-flash" };

                // Migrate API key from JSON to Credential Manager
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    _credentialService.SetApiKey("gemini", settings.ApiKey);
                    settings.ApiKey = "";
                    _logger.LogInformation("Migrated API key from settings.json to Credential Manager");
                    // Re-save without the key
                    SaveSettings(settings);
                }

                // Load API key from Credential Manager
                settings.ApiKey = _credentialService.GetApiKey("gemini") ?? "";

                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                BackupCorruptFile(SettingsFile);
            }
        }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            // Save API key to credential manager, not to disk
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                _credentialService.SetApiKey("gemini", settings.ApiKey);

            EnsureDirectoryExists();

            // Write settings without the API key
            var savedKey = settings.ApiKey;
            settings.ApiKey = "";
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            settings.ApiKey = savedKey; // Restore in-memory
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    public List<CustomShortcut> LoadShortcuts()
    {
        EnsureDirectoryExists();
        if (File.Exists(ShortcutsFile))
        {
            try
            {
                string json = File.ReadAllText(ShortcutsFile);
                return JsonSerializer.Deserialize<List<CustomShortcut>>(json) ?? DefaultShortcuts();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load shortcuts");
                BackupCorruptFile(ShortcutsFile);
            }
        }

        // First run: create defaults and persist them
        var defaults = DefaultShortcuts();
        SaveShortcuts(defaults);
        return defaults;
    }

    public void SaveShortcuts(List<CustomShortcut> shortcuts)
    {
        try
        {
            EnsureDirectoryExists();
            string json = JsonSerializer.Serialize(shortcuts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ShortcutsFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save shortcuts");
        }
    }

    private static List<CustomShortcut> DefaultShortcuts() => new List<CustomShortcut>
    {
        new CustomShortcut { Keyword = "google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new CustomShortcut { Keyword = "yt", UrlTemplate = "https://www.youtube.com/results?search_query={query}" }
    };

    private void BackupCorruptFile(string filePath)
    {
        try
        {
            string backupPath = filePath + $".corrupt.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            File.Copy(filePath, backupPath, overwrite: true);
            _logger.LogWarning("Backed up corrupt file to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup corrupt file {FilePath}", filePath);
        }
    }

    private void MigrateIfNeeded()
    {
        if (!File.Exists(SettingsFile))
        {
            string cwdSettings = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(cwdSettings))
            {
                try { File.Copy(cwdSettings, SettingsFile); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to migrate settings"); }
            }
        }
        if (!File.Exists(ShortcutsFile))
        {
            string cwdShortcuts = Path.Combine(AppContext.BaseDirectory, "shortcuts.json");
            if (File.Exists(cwdShortcuts))
            {
                try { File.Copy(cwdShortcuts, ShortcutsFile); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to migrate shortcuts"); }
            }
        }
    }
}
