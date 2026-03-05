using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Compass.Services;

public class SettingsService
{
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass");
    private static readonly string SettingsFile = Path.Combine(DataPath, "settings.json");
    private static readonly string ShortcutsFile = Path.Combine(DataPath, "shortcuts.json");

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
                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Compass] LoadSettings: {ex.Message}");
            }
        }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            EnsureDirectoryExists();
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Compass] SaveSettings: {ex.Message}");
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
                Debug.WriteLine($"[Compass] LoadShortcuts: {ex.Message}");
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
            Debug.WriteLine($"[Compass] SaveShortcuts: {ex.Message}");
        }
    }

    private static List<CustomShortcut> DefaultShortcuts() => new List<CustomShortcut>
    {
        new CustomShortcut { Keyword = "google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new CustomShortcut { Keyword = "yt", UrlTemplate = "https://www.youtube.com/results?search_query={query}" }
    };

    /// <summary>One-time migration: copy CWD data files to %AppData%\Compass\ on first run.</summary>
    private void MigrateIfNeeded()
    {
        if (!File.Exists(SettingsFile))
        {
            string cwdSettings = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(cwdSettings))
            {
                try { File.Copy(cwdSettings, SettingsFile); }
                catch (Exception ex) { Debug.WriteLine($"[Compass] MigrateSettings: {ex.Message}"); }
            }
        }
        if (!File.Exists(ShortcutsFile))
        {
            string cwdShortcuts = Path.Combine(AppContext.BaseDirectory, "shortcuts.json");
            if (File.Exists(cwdShortcuts))
            {
                try { File.Copy(cwdShortcuts, ShortcutsFile); }
                catch (Exception ex) { Debug.WriteLine($"[Compass] MigrateShortcuts: {ex.Message}"); }
            }
        }
    }
}
