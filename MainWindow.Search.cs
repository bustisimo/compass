using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Compass.Services;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Compass;

/// <summary>
/// MainWindow - Search and input handling
/// </summary>
public partial class MainWindow
{
    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V: intercept for image paste
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var bitmapSource = System.Windows.Clipboard.GetImage();
                if (bitmapSource != null)
                {
                    byte[] pngBytes = BitmapSourceToPng(bitmapSource);
                    _pendingImages.Add((pngBytes, "image/png", "clipboard.png"));
                    UpdateAttachedImagesUI();
                    e.Handled = true;
                    return;
                }
            }
            else if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList();
                foreach (string? file in files)
                {
                    if (file != null && IsImageFile(file))
                    {
                        byte[] data = File.ReadAllBytes(file);
                        _pendingImages.Add((data, GetMimeType(file), System.IO.Path.GetFileName(file)));
                    }
                }
                if (_pendingImages.Count > 0)
                    UpdateAttachedImagesUI();
            }
        }

        if (e.Key == Key.Right && ResumeChatIndicator.Visibility == Visibility.Visible)
        {
            ResumeChat();
            e.Handled = true;
            return;
        }

        // Tab: action shortcuts on selected result (Feature 9)
        if (e.Key == Key.Tab)
        {
            if (SearchResultList.Visibility == Visibility.Visible && SearchResultList.SelectedItem is AppSearchResult tabResult)
            {
                ShowResultContextMenu(tabResult);
            }
            e.Handled = true;
            return;
        }

        // Delete key: delete selected chat history result
        if (e.Key == Key.Delete)
        {
            if (SearchResultList.Visibility == Visibility.Visible &&
                SearchResultList.SelectedItem is AppSearchResult delResult &&
                delResult.ResultType == ResultType.ChatHistory)
            {
                string chatPath = delResult.FilePath["CHAT:".Length..];
                try { File.Delete(chatPath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete saved chat: {File}", chatPath); }
                ShowSavedChatsAsResults();
                e.Handled = true;
                return;
            }
        }

        // Down / Ctrl+N: navigate results or widgets down
        if (e.Key == Key.Down || (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control))
        {
            if (SearchResultList.Visibility == Visibility.Visible && SearchResultList.Items.Count > 0)
            {
                int newIndex = SearchResultList.SelectedIndex + 1;
                if (newIndex >= SearchResultList.Items.Count) newIndex = 0;
                SearchResultList.SelectedIndex = newIndex;
                SearchResultList.ScrollIntoView(SearchResultList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        // Up / Ctrl+P: navigate results or widgets up
        if (e.Key == Key.Up || (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control))
        {
            if (SearchResultList.Visibility == Visibility.Visible && SearchResultList.Items.Count > 0)
            {
                int newIndex = SearchResultList.SelectedIndex - 1;
                if (newIndex < 0) newIndex = SearchResultList.Items.Count - 1;
                SearchResultList.SelectedIndex = newIndex;
                SearchResultList.ScrollIntoView(SearchResultList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            // If images are attached, skip search result dispatch — always go to chat
            if (_pendingImages.Count == 0 && SearchResultList.Visibility == Visibility.Visible && SearchResultList.Items.Count > 0)
            {
                var selectedItem = (AppSearchResult)(SearchResultList.SelectedItem ?? SearchResultList.Items[0]);
                LaunchApp(selectedItem);
                return;
            }

            string userText = InputBox.Text;
            if (string.IsNullOrWhiteSpace(userText)) return;

            // "/" prefix: direct command execution
            if (userText.StartsWith("/"))
            {
                string cmdName = userText[1..].Trim();
                var ext = _extensions.FirstOrDefault(e => e.TriggerName.Equals(cmdName, StringComparison.OrdinalIgnoreCase));
                if (ext != null)
                {
                    try
                    {
                        string output = _extService.ExecuteExtension(ext);
                        _logger.LogInformation("Command '/{TriggerName}': {Output}", ext.TriggerName, output);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Command '/{TriggerName}' failed", cmdName); }
                    InputBox.Clear();
                    this.Hide();
                    return;
                }
            }

            if (userText.Trim().Equals("shortcuts", StringComparison.OrdinalIgnoreCase))
            {
                EnterManagerMode("Shortcuts");
                return;
            }
            if (userText.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                EnterManagerMode("General");
                return;
            }

            var parts = userText.Split(' ', 2);
            var shortcut = _userShortcuts.FirstOrDefault(s => s.Keyword.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            if (shortcut != null)
            {
                string query = parts.Length > 1 ? parts[1] : "";
                string url = shortcut.UrlTemplate.Replace("{query}", Uri.EscapeDataString(query));
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch (Exception ex) { _ = ShowModernDialog("Shortcut Error", $"Failed to open shortcut URL: {ex.Message}"); }
                InputBox.Clear();
                this.Hide();
                return;
            }

            // Capture pending images before clearing
            var imagesToSend = _pendingImages.Count > 0
                ? _pendingImages.Select(i => (i.data, i.mimeType, i.fileName)).ToList()
                : null;
            _pendingImages.Clear();
            UpdateAttachedImagesUI();

            InputBox.Clear();
            InputBox.IsEnabled = false;
            AnimateToChatMode();

            if (imagesToSend != null && imagesToSend.Count > 0)
                AddChatBubbleWithImages("You", userText, imagesToSend.Select(i => i.data).ToList());
            else
                AddChatBubble("You", userText);

            if (await ProcessLocalCommand(userText))
            {
                InputBox.IsEnabled = true;
                InputBox.Focus();
                return;
            }

            await AskGeminiAsync(userText, imagesToSend?.Select(i => (i.data, i.mimeType)).ToList());
        }
    }

    private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateResumeIndicator();

        string query = InputBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            HideSearchList();
            if (ChatScroll.Visibility != Visibility.Visible)
                ShowWidgetPanel();
            return;
        }

        HideWidgetPanel();

        // "/" prefix: command palette — quick actions + extension commands
        if (query.StartsWith("/"))
        {
            string filter = query.Length > 1 ? query[1..] : "";
            var quickActions = _quickActionsService.Search(filter);
            var cmdResults = _searchService.SearchCommands(filter);
            var merged = quickActions.Concat(cmdResults).ToList();
            if (merged.Any())
            {
                SearchResultList.ItemsSource = merged;
                ShowSearchList();
                SearchResultList.SelectedIndex = 0;
            }
            else
            {
                HideSearchList();
            }
            return;
        }

        // "clipboard" keyword
        if (query.Equals("clipboard", StringComparison.OrdinalIgnoreCase))
        {
            ShowClipboardPanel();
            return;
        }

        // "chats" keyword — show saved chat sessions
        if (query.Equals("chats", StringComparison.OrdinalIgnoreCase) ||
            query.Equals("chat history", StringComparison.OrdinalIgnoreCase))
        {
            ShowSavedChatsAsResults();
            return;
        }

        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        // Plugin search (includes calculator, clipboard, snippets, file search, bookmarks, recent files, quick toggles)
        var allResults = new List<AppSearchResult>();
        try
        {
            var pluginResults = await _pluginHost.SearchAllAsync(query);
            if (token.IsCancellationRequested) return;
            allResults.AddRange(pluginResults);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Plugin search failed"); }

        // Standard app/command search
        var appResults = _searchService.Search(query, _geminiService.HasHistory);
        if (token.IsCancellationRequested) return;
        allResults.AddRange(appResults);

        if (allResults.Any())
        {
            SearchResultList.ItemsSource = allResults;
            ShowSearchList();
            SearchResultList.SelectedIndex = 0;
        }
        else if (query.Length > 3)
        {
            // AI suggestion fallback (Feature 7)
            SearchResultList.ItemsSource = new List<AppSearchResult>
            {
                new AppSearchResult
                {
                    AppName = $"Ask AI about '{query}'",
                    FilePath = $"AI_SUGGEST:{query}",
                    ResultType = ResultType.AiSuggestion,
                    Subtitle = "No results \u2014 press Enter to ask AI",
                    GeometryIcon = Geometry.Parse("M21.5,11.5L14.5,4.5L12,7L15,10H4V12H15L12,15L14.5,17.5L21.5,11.5Z")
                }
            };
            ShowSearchList();
            SearchResultList.SelectedIndex = 0;
        }
        else
        {
            HideSearchList();
        }
    }

    // ---------------------------------------------------------------------------
    // App launching
    // ---------------------------------------------------------------------------

    private void SearchResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is AppSearchResult result)
            LaunchApp(result);
    }

    private void SearchResultList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is AppSearchResult result)
        {
            ShowResultContextMenu(result);
            e.Handled = true;
        }
    }

    private async void LaunchApp(AppSearchResult selectedItem)
    {
        if (string.IsNullOrEmpty(selectedItem.FilePath)) return;

        if (selectedItem.FilePath == "MATH")
        {
            System.Windows.Clipboard.SetText(selectedItem.AppName);
            this.Hide();
            InputBox.Clear();
            return;
        }

        // AI suggestion fallback — enter chat mode
        if (selectedItem.FilePath.StartsWith("AI_SUGGEST:"))
        {
            string query = selectedItem.FilePath["AI_SUGGEST:".Length..];
            InputBox.Clear();
            InputBox.IsEnabled = false;
            AnimateToChatMode();
            AddChatBubble("You", query);
            if (!await ProcessLocalCommand(query))
                await AskGeminiAsync(query);
            return;
        }

        if (selectedItem.FilePath.StartsWith("COMMAND:"))
        {
            if (selectedItem.FilePath == "COMMAND:SHORTCUTS") { EnterManagerMode("Shortcuts"); SearchResultList.Visibility = Visibility.Collapsed; return; }
            if (selectedItem.FilePath == "COMMAND:SETTINGS") { EnterManagerMode("General"); SearchResultList.Visibility = Visibility.Collapsed; return; }
            if (selectedItem.FilePath == "COMMAND:COMMANDS") { EnterManagerMode("Commands"); SearchResultList.Visibility = Visibility.Collapsed; return; }
            if (selectedItem.FilePath == "COMMAND:RESUME")
            {
                InputBox.Clear();
                AnimateToChatMode();
                return;
            }
            if (selectedItem.FilePath is "COMMAND:CLEAR_CHAT" or "COMMAND:NEW_CHAT")
            {
                ChatPanel.Children.Clear();
                _geminiService.ClearHistory();
                InputBox.Clear();
                SearchResultList.Visibility = Visibility.Collapsed;
                AnimateToSpotlightMode();
                return;
            }
            ExecuteSystemCommand(selectedItem.FilePath);
            InputBox.Clear();
            this.Hide();
            return;
        }

        if (selectedItem.FilePath.StartsWith("EXTENSION:"))
        {
            string trigger = selectedItem.FilePath.Substring("EXTENSION:".Length);
            var ext = _extensions.FirstOrDefault(e => e.TriggerName == trigger);
            if (ext != null)
            {
                try
                {
                    string output = _extService.ExecuteExtension(ext);
                    _logger.LogInformation("Extension '{TriggerName}': {Output}", ext.TriggerName, output);
                }
                catch (Exception ex) { AddChatBubble("System", $"Extension Error: {ex.Message}"); }
            }
            this.Hide();
            InputBox.Clear();
            return;
        }

        if (selectedItem.FilePath.StartsWith("CHAT:"))
        {
            string chatFile = selectedItem.FilePath["CHAT:".Length..];
            InputBox.Clear();
            SearchResultList.Visibility = Visibility.Collapsed;
            LoadChatFromFile(chatFile, selectedItem.AppName);
            return;
        }

        if (selectedItem.FilePath.StartsWith("SHORTCUT:"))
        {
            InputBox.Text = selectedItem.AppName + " ";
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
            return;
        }

        // Try plugin execution first (handles CLIPBOARD:*, SNIPPET:*, BOOKMARK:*, etc.)
        try
        {
            var pluginResult = await _pluginHost.ExecuteAsync(selectedItem);
            if (pluginResult != null)
            {
                InputBox.Clear();
                SearchResultList.Visibility = Visibility.Collapsed;
                this.Hide();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plugin execution failed for {FilePath}", selectedItem.FilePath);
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = selectedItem.FilePath, UseShellExecute = true });
            _searchService.RecordLaunch(selectedItem.AppName);
        }
        catch (Exception ex) { AddChatBubble("System", $"Error: {ex.Message}"); }

        InputBox.Clear();
        SearchResultList.Visibility = Visibility.Collapsed;
        this.Hide();
    }

    // ---------------------------------------------------------------------------
    // Local commands (non-AI)
    // ---------------------------------------------------------------------------

    private async Task<bool> ProcessLocalCommand(string input)
    {
        string lowerInput = input.ToLower().Trim();

        if (lowerInput.StartsWith("create command "))
        {
            var parts = input.Split(' ', 3);
            if (parts.Length == 3)
            {
                await GenerateExtensionAsync(parts[2], parts[1]);
                return true;
            }
        }

        if (lowerInput == "clear" || lowerInput == "clear chat")
        {
            ChatPanel.Children.Clear();
            _geminiService.ClearHistory();
            AnimateToSpotlightMode();
            return true;
        }

        if (lowerInput == "dark mode")
        {
            try
            {
                const string key = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                Registry.SetValue(key, "AppsUseLightTheme", 0);
                Registry.SetValue(key, "SystemUsesLightTheme", 0);
                AddChatBubble("System", "Switched to Dark Mode.");
            }
            catch (Exception ex) { AddChatBubble("System", $"Error: {ex.Message}"); }
            return true;
        }

        if (lowerInput == "light mode")
        {
            try
            {
                const string key = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                Registry.SetValue(key, "AppsUseLightTheme", 1);
                Registry.SetValue(key, "SystemUsesLightTheme", 1);
                AddChatBubble("System", "Switched to Light Mode.");
            }
            catch (Exception ex) { AddChatBubble("System", $"Error: {ex.Message}"); }
            return true;
        }

        if (lowerInput == "lock screen")
        {
            try
            {
                Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                AddChatBubble("System", "Locking screen...");
            }
            catch (Exception ex) { AddChatBubble("System", $"Error: {ex.Message}"); }
            return true;
        }

        // Media / system text triggers
        if (lowerInput == "play" || lowerInput == "pause" || lowerInput == "play pause")
        {
            _systemCommandService.MediaPlayPause();
            AddChatBubble("System", "Toggled Play/Pause.");
            return true;
        }
        if (lowerInput == "next track")
        {
            _systemCommandService.MediaNextTrack();
            AddChatBubble("System", "Skipped to next track.");
            return true;
        }
        if (lowerInput == "previous track" || lowerInput == "prev track")
        {
            _systemCommandService.MediaPrevTrack();
            AddChatBubble("System", "Went to previous track.");
            return true;
        }
        if (lowerInput == "volume up")
        {
            _systemCommandService.VolumeUp();
            AddChatBubble("System", "Volume increased.");
            return true;
        }
        if (lowerInput == "volume down")
        {
            _systemCommandService.VolumeDown();
            AddChatBubble("System", "Volume decreased.");
            return true;
        }
        if (lowerInput == "mute" || lowerInput == "unmute")
        {
            _systemCommandService.VolumeMute();
            AddChatBubble("System", "Toggled mute.");
            return true;
        }

        return false;
    }

    private void ExecuteSystemCommand(string commandPath)
    {
        try
        {
            switch (commandPath)
            {
                case "COMMAND:MEDIA_PLAY_PAUSE": _systemCommandService.MediaPlayPause(); break;
                case "COMMAND:MEDIA_NEXT": _systemCommandService.MediaNextTrack(); break;
                case "COMMAND:MEDIA_PREV": _systemCommandService.MediaPrevTrack(); break;
                case "COMMAND:VOLUME_UP": _systemCommandService.VolumeUp(); break;
                case "COMMAND:VOLUME_DOWN": _systemCommandService.VolumeDown(); break;
                case "COMMAND:VOLUME_MUTE": _systemCommandService.VolumeMute(); break;
                case "COMMAND:TOGGLE_WIFI": _systemCommandService.ToggleWifi(); break;
                case "COMMAND:TOGGLE_BLUETOOTH": _systemCommandService.ToggleBluetooth(); break;
                case "COMMAND:DO_NOT_DISTURB": _systemCommandService.OpenDoNotDisturb(); break;
                case "COMMAND:WINDOW_MINIMIZE": _systemCommandService.MinimizeActiveWindow(); break;
                case "COMMAND:WINDOW_MAXIMIZE": _systemCommandService.MaximizeActiveWindow(); break;
                case "COMMAND:SNAP_LEFT": _systemCommandService.SnapWindowLeft(); break;
                case "COMMAND:SNAP_RIGHT": _systemCommandService.SnapWindowRight(); break;
                case "COMMAND:LAYOUT_SPLIT": _systemCommandService.LayoutSplit(); break;
                case "COMMAND:LAYOUT_STACK": _systemCommandService.LayoutStack(); break;
                case "COMMAND:LAYOUT_THIRDS": _systemCommandService.LayoutThirds(); break;
                case "COMMAND:TOGGLE_NIGHTLIGHT":
                    Process.Start(new ProcessStartInfo { FileName = "ms-settings:nightlight", UseShellExecute = true });
                    break;
                case "COMMAND:TOGGLE_AIRPLANE":
                    Process.Start(new ProcessStartInfo { FileName = "ms-settings:network-airplanemode", UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System command failed: {CommandPath}", commandPath);
        }
    }

    // ---------------------------------------------------------------------------
    // Search preview panel
    // ---------------------------------------------------------------------------

    private void SearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultList.SelectedItem is not AppSearchResult result)
        {
            SearchPreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewContent.Children.Clear();
        bool hasPreview = false;

        switch (result.ResultType)
        {
            case ResultType.Application:
                {
                    var nameText = new TextBlock
                    {
                        Text = result.AppName,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FindResource("TextPrimaryBrush") as Brush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    PreviewContent.Children.Add(nameText);

                    if (!string.IsNullOrEmpty(result.FilePath) && !result.FilePath.StartsWith("COMMAND:"))
                    {
                        var pathText = new TextBlock
                        {
                            Text = result.FilePath,
                            FontSize = 11,
                            Foreground = FindResource("TextTertiaryBrush") as Brush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        PreviewContent.Children.Add(pathText);

                        try
                        {
                            if (File.Exists(result.FilePath))
                            {
                                var fi = new FileInfo(result.FilePath);
                                var metaText = new TextBlock
                                {
                                    Text = $"Size: {FormatFileSize(fi.Length)}  |  Modified: {fi.LastWriteTime:MMM d, yyyy h:mm tt}",
                                    FontSize = 11,
                                    Foreground = FindResource("TextTertiaryBrush") as Brush,
                                    Margin = new Thickness(0, 2, 0, 0)
                                };
                                PreviewContent.Children.Add(metaText);
                            }
                        }
                        catch { }
                    }
                    hasPreview = true;
                }
                break;

            case ResultType.File:
            case ResultType.RecentFile:
                {
                    var nameText = new TextBlock
                    {
                        Text = result.AppName,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FindResource("TextPrimaryBrush") as Brush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    PreviewContent.Children.Add(nameText);

                    if (!string.IsNullOrEmpty(result.FilePath))
                    {
                        var pathText = new TextBlock
                        {
                            Text = result.FilePath,
                            FontSize = 11,
                            Foreground = FindResource("TextTertiaryBrush") as Brush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        PreviewContent.Children.Add(pathText);

                        try
                        {
                            if (File.Exists(result.FilePath))
                            {
                                var fi = new FileInfo(result.FilePath);
                                var metaText = new TextBlock
                                {
                                    Text = $"Size: {FormatFileSize(fi.Length)}  |  Modified: {fi.LastWriteTime:MMM d, yyyy h:mm tt}",
                                    FontSize = 11,
                                    Foreground = FindResource("TextTertiaryBrush") as Brush,
                                    Margin = new Thickness(0, 2, 0, 0)
                                };
                                PreviewContent.Children.Add(metaText);
                            }
                        }
                        catch { }
                    }
                    hasPreview = true;
                }
                break;

            case ResultType.ClipboardHistory:
                {
                    var contentText = new TextBox
                    {
                        Text = result.PreviewContent ?? result.AppName,
                        FontSize = 12,
                        Foreground = FindResource("TextSecondaryBrush") as Brush,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        TextWrapping = TextWrapping.Wrap,
                        IsReadOnly = true,
                        MaxHeight = 120,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    PreviewContent.Children.Add(contentText);
                    hasPreview = true;
                }
                break;

            case ResultType.Bookmark:
                {
                    var titleText = new TextBlock
                    {
                        Text = result.AppName,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FindResource("TextPrimaryBrush") as Brush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    PreviewContent.Children.Add(titleText);

                    if (!string.IsNullOrEmpty(result.FilePath))
                    {
                        string url = result.FilePath.StartsWith("BOOKMARK:") ? result.FilePath["BOOKMARK:".Length..] : result.FilePath;
                        var urlText = new TextBlock
                        {
                            Text = url,
                            FontSize = 11,
                            Foreground = FindResource("AccentBrush") as Brush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        PreviewContent.Children.Add(urlText);
                    }
                    hasPreview = true;
                }
                break;

            case ResultType.Snippet:
                {
                    var kwText = new TextBlock
                    {
                        Text = result.AppName,
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FindResource("TextPrimaryBrush") as Brush
                    };
                    PreviewContent.Children.Add(kwText);

                    if (!string.IsNullOrEmpty(result.PreviewContent))
                    {
                        var contentText = new TextBox
                        {
                            Text = result.PreviewContent,
                            FontSize = 12,
                            Foreground = FindResource("TextSecondaryBrush") as Brush,
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            TextWrapping = TextWrapping.Wrap,
                            IsReadOnly = true,
                            MaxHeight = 100,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        PreviewContent.Children.Add(contentText);
                    }
                    hasPreview = true;
                }
                break;
        }

        if (hasPreview)
        {
            SearchPreviewPanel.Visibility = Visibility.Visible;
            if (_appSettings.AnimationsEnabled)
            {
                SearchPreviewPanel.Opacity = 0;
                SearchPreviewPanel.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
            }
        }
        else
        {
            SearchPreviewPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    // ---------------------------------------------------------------------------
    // Search bar focus glow
    // ---------------------------------------------------------------------------

    private void InputBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_appSettings.AnimationsEnabled) return;
        var accentBrush = FindResource("AccentBrush") as SolidColorBrush;
        if (accentBrush == null) return;
        var glowColor = accentBrush.Color;
        var anim = new ColorAnimation(
            Color.FromArgb(0x60, glowColor.R, glowColor.G, glowColor.B),
            TimeSpan.FromSeconds(0.25))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var brush = new SolidColorBrush();
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        SearchBarBorder.BorderBrush = brush;
        SearchBarBorder.BorderThickness = new Thickness(1.5);
    }

    private void InputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_appSettings.AnimationsEnabled)
        {
            SearchBarBorder.BorderBrush = Brushes.Transparent;
            SearchBarBorder.BorderThickness = new Thickness(0);
            return;
        }
        var anim = new ColorAnimation(
            Colors.Transparent,
            TimeSpan.FromSeconds(0.2))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var brush = SearchBarBorder.BorderBrush as SolidColorBrush;
        if (brush == null || brush.IsFrozen)
        {
            brush = new SolidColorBrush();
            SearchBarBorder.BorderBrush = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        SearchBarBorder.BorderThickness = new Thickness(1.5);
    }

    // ---------------------------------------------------------------------------
    // Search list animations
    // ---------------------------------------------------------------------------

    private void ShowSearchList()
    {
        if (SearchScale.ScaleY == 1) return;
        SearchResultList.Visibility = Visibility.Visible;
        AnimateOrSnap(SearchScale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromSeconds(0.2),
            new CubicEase { EasingMode = EasingMode.EaseOut });

        // Staggered fade-in for each search result item
        if (_appSettings.AnimationsEnabled)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                for (int i = 0; i < SearchResultList.Items.Count; i++)
                {
                    var container = SearchResultList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (container == null) continue;
                    container.Opacity = 0;
                    var translate = new TranslateTransform(0, 8);
                    container.RenderTransform = translate;

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 30)
                    };
                    var slideUp = new DoubleAnimation(8, 0, TimeSpan.FromSeconds(0.15))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 30),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    container.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
                }
            });
        }
    }

    private void HideSearchList()
    {
        SearchPreviewPanel.Visibility = Visibility.Collapsed;
        if (SearchScale.ScaleY == 0) return;
        AnimateOrSnap(SearchScale, ScaleTransform.ScaleYProperty, 0, TimeSpan.FromSeconds(0.2),
            new CubicEase { EasingMode = EasingMode.EaseIn },
            () => SearchResultList.Visibility = Visibility.Collapsed);
    }

    // ---------------------------------------------------------------------------
    // Saved chats as search results
    // ---------------------------------------------------------------------------

    private void ShowSavedChatsAsResults()
    {
        var chatIcon = Geometry.Parse("M20,2H4A2,2 0 0,0 2,4V22L6,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2Z");
        var results = new List<AppSearchResult>();

        if (Directory.Exists(ChatSavesFolder))
        {
            var files = Directory.GetFiles(ChatSavesFolder, "*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(10)
                .ToArray();

            foreach (var file in files)
            {
                string rawName = System.IO.Path.GetFileNameWithoutExtension(file);
                // Strip timestamp prefix (yyyyMMdd-HHmmss_)
                string displayName = rawName;
                if (rawName.Length > 16 && rawName[8] == '-' && rawName[15] == '_')
                    displayName = rawName[16..].Replace("-", " ").Replace("_", " ");

                string date = File.GetLastWriteTime(file).ToString("MMM d, h:mm tt");

                results.Add(new AppSearchResult
                {
                    AppName = displayName,
                    FilePath = $"CHAT:{file}",
                    GeometryIcon = chatIcon,
                    Subtitle = $"{date}  \u2022  Press Del to delete",
                    ResultType = ResultType.ChatHistory
                });
            }
        }

        if (results.Count == 0)
            return; // No saved chats — do nothing

        SearchResultList.ItemsSource = results;
        ShowSearchList();
        SearchResultList.SelectedIndex = 0;
    }
}
