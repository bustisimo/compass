namespace Compass.Services.Interfaces;

public interface ISettingsService
{
    void EnsureDirectoryExists();
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    List<CustomShortcut> LoadShortcuts();
    void SaveShortcuts(List<CustomShortcut> shortcuts);
}
