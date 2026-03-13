using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    // Curated model lists with marketing names
    private static readonly List<ImageModelOption> _imageModelOptions = new()
    {
        new("Nano Banana", "gemini-2.5-flash-image"),
        new("Nano Banana Pro", "gemini-3-pro-image-preview"),
        new("Nano Banana 2", "gemini-3.1-flash-image-preview"),
    };

    private static readonly Dictionary<string, string> _chatModelDisplayNames = new()
    {
        { "gemini-2.5-flash", "Gemini 2.5 Flash" },
        { "gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite" },
        { "gemini-2.5-pro", "Gemini 2.5 Pro" },
        { "gemini-3-flash-preview", "Gemini 3 Flash" },
        { "gemini-3.1-pro-preview", "Gemini 3.1 Pro" },
        { "gemini-3.1-flash-lite-preview", "Gemini 3.1 Flash-Lite" },
    };

    private static string GetChatModelDisplayName(string modelId)
    {
        return _chatModelDisplayNames.TryGetValue(modelId, out var name) ? name : modelId;
    }

    private static string GetShortModelLabel(string modelId)
    {
        return modelId switch
        {
            "gemini-2.5-flash" => "2.5 Flash",
            "gemini-2.5-flash-lite" => "2.5 Flash-Lite",
            "gemini-2.5-pro" => "2.5 Pro",
            "gemini-3-flash-preview" => "3 Flash",
            "gemini-3.1-pro-preview" => "3.1 Pro",
            "gemini-3.1-flash-lite-preview" => "3.1 Flash-Lite",
            _ => modelId.Replace("gemini-", "").Replace("-preview", "")
        };
    }

    private void RefreshRoutingModelLists()
    {
        FastModelComboBox.ItemsSource = null;
        FastModelComboBox.ItemsSource = _appSettings.AvailableModels;
        FastModelComboBox.SelectedItem = _appSettings.FastModel;

        PowerModelComboBox.ItemsSource = null;
        PowerModelComboBox.ItemsSource = _appSettings.AvailableModels;
        PowerModelComboBox.SelectedItem = _appSettings.PowerModel;

        RefreshImageModelList();
    }

    private void RefreshImageModelList()
    {
        ImageModelComboBox.ItemsSource = null;
        ImageModelComboBox.ItemsSource = _imageModelOptions;
        ImageModelComboBox.SelectedValue = _appSettings.ImageGenerationModel;

        // If current model isn't in the curated list, select the first option
        if (ImageModelComboBox.SelectedValue == null && _imageModelOptions.Count > 0)
        {
            ImageModelComboBox.SelectedIndex = 0;
            _appSettings.ImageGenerationModel = _imageModelOptions[0].ModelId;
        }
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

    private void ApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _appSettings.ApiKey = ApiKeyBox.Password;
        SaveSettings();
    }

    private void SystemPromptBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _appSettings.SystemPrompt = SystemPromptBox.Text;
        SaveSettings();
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
        if (sender is System.Windows.Controls.CheckBox cb)
            _appSettings.RandomGreetingsEnabled = cb.IsChecked == true;
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

    private void ImageModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageModelComboBox.SelectedValue is string modelId)
        {
            _appSettings.ImageGenerationModel = modelId;
            SaveSettings();
        }
    }

    // ---------------------------------------------------------------------------
    // Chat bar model picker
    // ---------------------------------------------------------------------------

    private bool _suppressModelPickerOpen;

    private void EllipsisMenu_Click(object sender, MouseButtonEventArgs e)
    {
        if (_suppressModelPickerOpen)
            return;

        BuildModelPickerPopup();
        ModelPickerPopup.IsOpen = true;
        e.Handled = true;
    }

    private void ModelPickerPopup_Closed(object? sender, EventArgs e)
    {
        _suppressModelPickerOpen = true;
        Dispatcher.BeginInvoke(() => _suppressModelPickerOpen = false);
    }

    private void ChatsBtn_Click(object sender, MouseButtonEventArgs e)
    {
        ShowSavedChatsAsResults();
    }

    private void UpdateModelSelectorLabel()
    {
        // No longer needed as standalone label — kept for settings sync
    }

    private void BuildModelPickerPopup()
    {
        ModelPickerPanel.Children.Clear();

        // --- Image Generation Toggle ---
        var toggleRow = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        var toggleLabel = new StackPanel();
        toggleLabel.Children.Add(new TextBlock
        {
            Text = "Image Generation",
            FontSize = 12.5,
            Foreground = Resources["TextPrimaryBrush"] as Brush,
            FontWeight = FontWeights.SemiBold
        });
        toggleLabel.Children.Add(new TextBlock
        {
            Text = _imageGenModeEnabled ? "Always generate images" : "Auto-detect from prompt",
            FontSize = 9.5,
            Foreground = Resources["TextTertiaryBrush"] as Brush,
            Margin = new Thickness(0, 1, 0, 0)
        });

        var toggleSwitch = new CheckBox
        {
            IsChecked = _imageGenModeEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Resources["ToggleSwitchStyle"] as Style
        };
        toggleSwitch.Checked += (s, ev) =>
        {
            _imageGenModeEnabled = true;
            BuildModelPickerPopup(); // rebuild to update subtitle
        };
        toggleSwitch.Unchecked += (s, ev) =>
        {
            _imageGenModeEnabled = false;
            BuildModelPickerPopup();
        };

        DockPanel.SetDock(toggleSwitch, Dock.Right);
        toggleRow.Children.Add(toggleSwitch);
        toggleRow.Children.Add(toggleLabel);
        ModelPickerPanel.Children.Add(toggleRow);

        // --- Separator ---
        ModelPickerPanel.Children.Add(new Border
        {
            Height = 1,
            Background = Resources["InputBorderBrush"] as Brush,
            Margin = new Thickness(8, 6, 8, 6)
        });

        // Partition chat models into flagship (has display name) and other
        var flagshipModels = new List<string>();
        var otherModels = new List<string>();

        foreach (var modelId in _appSettings.AvailableModels)
        {
            if (modelId.Contains("-image")) continue; // skip image models from chat list
            if (_chatModelDisplayNames.ContainsKey(modelId))
                flagshipModels.Add(modelId);
            else
                otherModels.Add(modelId);
        }

        // --- Chat Models section ---
        AddSectionHeader("CHAT MODEL");

        foreach (var modelId in flagshipModels)
        {
            string displayName = _chatModelDisplayNames[modelId];
            bool isSelected = modelId == _appSettings.SelectedModel;
            AddModelItem(displayName, modelId, isSelected, () =>
            {
                _appSettings.SelectedModel = modelId;
                SaveSettings();
                RefreshModelList();
                UpdateModelSelectorLabel();
                ModelPickerPopup.IsOpen = false;
            });
        }

        // --- Other Models (collapsed by default) ---
        if (otherModels.Count > 0)
        {
            var otherPanel = new StackPanel { Visibility = Visibility.Collapsed };

            foreach (var modelId in otherModels)
            {
                bool isSelected = modelId == _appSettings.SelectedModel;
                var item = CreateModelPickerItem(modelId, modelId, isSelected, () =>
                {
                    _appSettings.SelectedModel = modelId;
                    SaveSettings();
                    RefreshModelList();
                    UpdateModelSelectorLabel();
                    ModelPickerPopup.IsOpen = false;
                });
                otherPanel.Children.Add(item);
            }

            // If current model is in "other", expand by default
            bool currentInOther = otherModels.Contains(_appSettings.SelectedModel);
            if (currentInOther) otherPanel.Visibility = Visibility.Visible;

            var toggleText = new TextBlock
            {
                Text = currentInOther ? "Hide other models" : $"Other models ({otherModels.Count})",
                FontSize = 10.5,
                Foreground = Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue,
                Cursor = Cursors.Hand,
                Margin = new Thickness(10, 6, 10, 2)
            };
            toggleText.MouseLeftButtonUp += (s, e) =>
            {
                if (otherPanel.Visibility == Visibility.Visible)
                {
                    otherPanel.Visibility = Visibility.Collapsed;
                    toggleText.Text = $"Other models ({otherModels.Count})";
                }
                else
                {
                    otherPanel.Visibility = Visibility.Visible;
                    toggleText.Text = "Hide other models";
                }
                e.Handled = true;
            };

            ModelPickerPanel.Children.Add(toggleText);
            ModelPickerPanel.Children.Add(otherPanel);
        }

        // --- Separator ---
        ModelPickerPanel.Children.Add(new Border
        {
            Height = 1,
            Background = Resources["InputBorderBrush"] as Brush,
            Margin = new Thickness(8, 6, 8, 6)
        });

        // --- Image Models section ---
        AddSectionHeader("IMAGE MODEL");

        foreach (var option in _imageModelOptions)
        {
            bool isSelected = option.ModelId == _appSettings.ImageGenerationModel;
            AddModelItem(option.DisplayName, option.ModelId, isSelected, () =>
            {
                _appSettings.ImageGenerationModel = option.ModelId;
                SaveSettings();
                RefreshImageModelList();
                ModelPickerPopup.IsOpen = false;
            });
        }
    }

    private void AddSectionHeader(string text)
    {
        ModelPickerPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Resources["TextTertiaryBrush"] as Brush,
            Margin = new Thickness(10, 6, 10, 4)
        });
    }

    private void AddModelItem(string displayName, string modelId, bool isSelected, Action onClick)
    {
        ModelPickerPanel.Children.Add(CreateModelPickerItem(displayName, modelId, isSelected, onClick));
    }

    private Border CreateModelPickerItem(string displayName, string modelId, bool isSelected, Action onClick)
    {
        var label = new TextBlock
        {
            Text = displayName,
            FontSize = 12.5,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isSelected
                ? (Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue)
                : (Resources["TextPrimaryBrush"] as Brush ?? Brushes.White)
        };

        var subtitle = new TextBlock
        {
            Text = modelId,
            FontSize = 9.5,
            Foreground = Resources["TextTertiaryBrush"] as Brush,
            Margin = new Thickness(0, 1, 0, 0)
        };

        // Check mark for selected item
        var checkMark = new TextBlock
        {
            Text = "\u2713",
            FontSize = 13,
            Foreground = Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
        };

        var textStack = new StackPanel();
        textStack.Children.Add(label);
        textStack.Children.Add(subtitle);

        var row = new DockPanel();
        DockPanel.SetDock(checkMark, Dock.Left);
        row.Children.Add(checkMark);
        row.Children.Add(textStack);

        var container = new Border
        {
            Padding = new Thickness(8, 5, 10, 5),
            CornerRadius = new CornerRadius(6),
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(20, 76, 194, 255))
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = row
        };

        container.MouseEnter += (s, e) =>
        {
            if (!isSelected)
                container.Background = Resources["HoverBrush"] as Brush ?? Brushes.DarkGray;
        };
        container.MouseLeave += (s, e) =>
        {
            if (!isSelected)
                container.Background = Brushes.Transparent;
        };
        container.MouseLeftButtonUp += (s, e) =>
        {
            onClick();
            e.Handled = true;
        };

        return container;
    }

}

public record ImageModelOption(string DisplayName, string ModelId);
