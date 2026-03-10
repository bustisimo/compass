using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Compass;

/// <summary>
/// MainWindow - Settings manager UI, shortcuts, commands
/// </summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------------------
    // Manager mode
    // ---------------------------------------------------------------------------

    private void EnterManagerMode(string tabName = "General")
    {
        SpotlightView.Visibility = Visibility.Collapsed;
        ManagerView.Visibility = Visibility.Visible;
        ShortcutsList.ItemsSource = null;
        ShortcutsList.ItemsSource = _userShortcuts;
        CommandsList.ItemsSource = null;
        CommandsList.ItemsSource = _extensions;
        UpdateEmptyStates();
        ApiKeyBox.Password = _appSettings.ApiKey;
        StartupCheck.IsChecked = _appSettings.LaunchAtStartup;
        RandomGreetingsCheck.IsChecked = _appSettings.RandomGreetingsEnabled;
        OpacitySlider.Value = _appSettings.WindowOpacity;
        SystemPromptBox.Text = _appSettings.SystemPrompt;
        RefreshModelList();
        RefreshRoutingModelLists();
        FireAndForget(FetchAvailableModelsAsync(), "FetchAvailableModelsAsync");

        // Sync routing controls
        SmartRoutingCheck.IsChecked = _appSettings.SmartRoutingEnabled;
        RoutingModelsPanel.Visibility = _appSettings.SmartRoutingEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Sync widget controls
        SyncWidgetControls();

        // Sync personalization controls
        SyncPersonalizationControls();

        foreach (TabItem tab in SettingsTabs.Items)
        {
            if ((string)tab.Header == tabName)
            {
                SettingsTabs.SelectedItem = tab;
                break;
            }
        }
    }

    private void ExitManagerMode()
    {
        SaveShortcuts();
        ManagerView.Visibility = Visibility.Collapsed;
        SpotlightView.Visibility = Visibility.Visible;
        InputBox.Clear();
        InputBox.Focus();
    }

    private void ExitManagerMode_Click(object sender, RoutedEventArgs e) => ExitManagerMode();

    private void RefreshModelList()
    {
        ModelComboBox.ItemsSource = null;
        ModelComboBox.ItemsSource = _appSettings.AvailableModels;
        ModelComboBox.SelectedItem = _appSettings.SelectedModel;
    }

    private void RefreshRoutingModelLists()
    {
        FastModelComboBox.ItemsSource = null;
        FastModelComboBox.ItemsSource = _appSettings.AvailableModels;
        FastModelComboBox.SelectedItem = _appSettings.FastModel;

        PowerModelComboBox.ItemsSource = null;
        PowerModelComboBox.ItemsSource = _appSettings.AvailableModels;
        PowerModelComboBox.SelectedItem = _appSettings.PowerModel;
    }

    private async Task FetchAvailableModelsAsync()
    {
        try
        {
            var models = await _geminiService.FetchAvailableModelsAsync(_appSettings);
            if (models.Count > 0)
            {
                _appSettings.AvailableModels = models;

                // Auto-correct selected model if it's no longer available
                if (!models.Contains(_appSettings.SelectedModel))
                {
                    _appSettings.SelectedModel = models.FirstOrDefault(m => m.Contains("gemini-2.5-flash") && !m.Contains("exp") && !m.Contains("lite"))
                        ?? models.FirstOrDefault(m => m.Contains("flash"))
                        ?? models.First();
                }

                // Auto-correct routing models too
                if (!models.Contains(_appSettings.FastModel))
                    _appSettings.FastModel = models.FirstOrDefault(m => m.Contains("flash-lite")) ?? _appSettings.SelectedModel;
                if (!models.Contains(_appSettings.PowerModel))
                    _appSettings.PowerModel = models.FirstOrDefault(m => m.Contains("2.5-flash") || m.Contains("pro")) ?? _appSettings.SelectedModel;

                RefreshModelList();
                RefreshRoutingModelLists();
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models");
            // Keep showing the stale model list; no UI disruption needed
        }
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedItem is string model)
        {
            _appSettings.SelectedModel = model;
            SaveSettings();
        }
    }

    // ---------------------------------------------------------------------------
    // Shortcut management
    // ---------------------------------------------------------------------------

    private void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NewKeywordBox.Text) && !string.IsNullOrWhiteSpace(NewUrlBox.Text))
        {
            _userShortcuts.Add(new CustomShortcut { Keyword = NewKeywordBox.Text.Trim(), UrlTemplate = NewUrlBox.Text.Trim() });
            NewKeywordBox.Clear();
            NewUrlBox.Clear();
            ShortcutsList.ItemsSource = null;
            ShortcutsList.ItemsSource = _userShortcuts;
            _searchService.RefreshShortcutCache(_userShortcuts);
            SaveShortcuts();
            UpdateEmptyStates();
        }
    }

    private void DeleteShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CustomShortcut shortcut)
        {
            _userShortcuts.Remove(shortcut);
            ShortcutsList.ItemsSource = null;
            ShortcutsList.ItemsSource = _userShortcuts;
            _searchService.RefreshShortcutCache(_userShortcuts);
            SaveShortcuts();
            UpdateEmptyStates();
        }
    }

    // ---------------------------------------------------------------------------
    // Command (Extension) management
    // ---------------------------------------------------------------------------

    private async void CreateCommand_Click(object sender, RoutedEventArgs e)
    {
        string name = NewCommandTrigger.Text.Trim();
        string intent = NewCommandIntent.Text.Trim();
        var btn = (Button)sender;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(intent))
        {
            var original = btn.Content;
            btn.Content = "Missing Info!";
            btn.IsEnabled = false;
            await Task.Delay(2000);
            btn.Content = original;
            btn.IsEnabled = true;
            return;
        }

        btn.IsEnabled = false;
        btn.Content = "Generating...";

        try
        {
            if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
            {
                await ShowModernDialog("API Key Required", "Please configure your API Key in the General settings first.");
                return;
            }

            string script = await _geminiService.GeneratePowerShellScriptAsync(intent, _appSettings);
            var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
            _extService.SaveExtension(ext);
            await RefreshExtensionCacheAsync();
            CommandsList.ItemsSource = null;
            CommandsList.ItemsSource = _extensions;
            UpdateEmptyStates();
            NewCommandTrigger.Clear();
            NewCommandIntent.Clear();
            btn.Content = "Created!";
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            btn.Content = "Error!";
            _logger.LogError(ex, "Failed to create command");
            await ShowModernDialog("Error", $"Failed to generate command: {ex.Message}");
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content = "Generate Command";
        }
    }

    private async void DeleteCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CompassExtension ext)
        {
            _extService.DeleteExtension(ext.TriggerName);
            await RefreshExtensionCacheAsync();
            CommandsList.ItemsSource = null;
            CommandsList.ItemsSource = _extensions;
            UpdateEmptyStates();
        }
    }

    // ---------------------------------------------------------------------------
    // Settings UI
    // ---------------------------------------------------------------------------

    private async void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.ApiKey = ApiKeyBox.Password;
        _appSettings.SystemPrompt = SystemPromptBox.Text;
        SaveSettings();

        if (sender is Button btn)
        {
            var originalContent = btn.Content;
            btn.Content = "Saved!";
            btn.IsEnabled = false;
            await Task.Delay(2000);
            btn.Content = originalContent;
            btn.IsEnabled = true;
        }
    }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        _appSettings.LaunchAtStartup = StartupCheck.IsChecked == true;
        SaveSettings();

        try
        {
            string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, true);
            if (_appSettings.LaunchAtStartup)
                key?.SetValue("Compass", Process.GetCurrentProcess().MainModule?.FileName ?? "");
            else
                key?.DeleteValue("Compass", false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update startup registry");
            _ = ShowModernDialog("Registry Error", $"Failed to update startup registration: {ex.Message}");
        }
    }

    private void RandomGreetingsCheck_Changed(object sender, RoutedEventArgs e)
    {
        _appSettings.RandomGreetingsEnabled = RandomGreetingsCheck.IsChecked == true;
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainBorder == null || !IsLoaded) return;
        _appSettings.WindowOpacity = OpacitySlider.Value;
        MainBorder.Opacity = _appSettings.WindowOpacity;

        // Debounce: only save 500ms after the last change
        if (_opacitySaveTimer == null)
        {
            _opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _opacitySaveTimer.Tick += (s, args) =>
            {
                _opacitySaveTimer.Stop();
                SaveSettings();
            };
        }
        _opacitySaveTimer.Stop();
        _opacitySaveTimer.Start();
    }

    // ---------------------------------------------------------------------------
    // Smart Model Routing Settings
    // ---------------------------------------------------------------------------

    private void SmartRoutingCheck_Changed(object sender, RoutedEventArgs e)
    {
        _appSettings.SmartRoutingEnabled = SmartRoutingCheck.IsChecked == true;
        RoutingModelsPanel.Visibility = _appSettings.SmartRoutingEnabled ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void FastModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FastModelComboBox.SelectedItem is string model)
        {
            _appSettings.FastModel = model;
            SaveSettings();
        }
    }

    private void PowerModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PowerModelComboBox.SelectedItem is string model)
        {
            _appSettings.PowerModel = model;
            SaveSettings();
        }
    }

}
