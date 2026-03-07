using System.Collections.ObjectModel;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.ViewModels;

public class ManagerViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IExtensionService _extensionService;
    private readonly IGeminiService _geminiService;
    private readonly ILogger<ManagerViewModel> _logger;

    private string _selectedTab = "General";

    public ManagerViewModel(
        ISettingsService settingsService,
        IExtensionService extensionService,
        IGeminiService geminiService,
        ILogger<ManagerViewModel> logger)
    {
        _settingsService = settingsService;
        _extensionService = extensionService;
        _geminiService = geminiService;
        _logger = logger;
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public ObservableCollection<CustomShortcut> Shortcuts { get; } = new();

    public ObservableCollection<CompassExtension> Extensions { get; } = new();

    public void LoadShortcuts(List<CustomShortcut> shortcuts)
    {
        Shortcuts.Clear();
        foreach (var s in shortcuts) Shortcuts.Add(s);
    }

    public void LoadExtensions(List<CompassExtension> extensions)
    {
        Extensions.Clear();
        foreach (var e in extensions) Extensions.Add(e);
    }

    public void AddShortcut(CustomShortcut shortcut)
    {
        Shortcuts.Add(shortcut);
    }

    public void RemoveShortcut(CustomShortcut shortcut)
    {
        Shortcuts.Remove(shortcut);
    }

    public void AddExtension(CompassExtension extension)
    {
        _extensionService.SaveExtension(extension);
        Extensions.Add(extension);
    }

    public void RemoveExtension(CompassExtension extension)
    {
        _extensionService.DeleteExtension(extension.TriggerName);
        Extensions.Remove(extension);
    }

    public void SaveShortcuts()
    {
        _settingsService.SaveShortcuts(Shortcuts.ToList());
    }

    public void SaveSettings(AppSettings settings)
    {
        _settingsService.SaveSettings(settings);
    }
}
