﻿using System.Runtime.InteropServices;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Google.GenAI;
using Google.GenAI.Types;

namespace Compass.Client
{
    public partial class MainWindow : Window
    {
        // --- API Configuration ---
        private static readonly List<Content> _chatHistory = new List<Content>();
        private List<CustomShortcut> _userShortcuts = new List<CustomShortcut>();
        private AppSettings _appSettings = new AppSettings();
        private List<AppSearchResult> _appCache = new();
        private List<AppSearchResult> _shortcutCache = new List<AppSearchResult>();
        private string _extensionsPath;
        private List<CompassExtension> _loadedExtensions = new List<CompassExtension>();

        // --- Native Windows API Constants ---
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_SPACE = 0x20;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_KEYMENU = 0xF100;

        // --- P/Invoke Signatures ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _windowHandle;
        private HwndSource? _source;

        public MainWindow()
        {
            InitializeComponent();
            _extensionsPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Compass", "Extensions");
            EnsureExtensionsFolderExists();
            LoadSettings();
            this.OpacitySlider.Value = _appSettings.WindowOpacity;
            LoadShortcuts();
            RefreshAppCache();
            this.SizeChanged += MainWindow_SizeChanged;
            this.Deactivated += MainWindow_Deactivated;
            this.MouseDown += Window_MouseDown;
            this.IsVisibleChanged += MainWindow_IsVisibleChanged;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
            {
                // Dynamically recalculate the center of the screen based on the new height
                this.Top = (SystemParameters.WorkArea.Height - this.ActualHeight) / 2;
                this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == false && ManagerView.Visibility == Visibility.Visible)
            {
                ExitManagerMode();
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            this.Hide();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var point = e.GetPosition(MainBorder);
                if (point.X < 0 || point.Y < 0 || point.X > MainBorder.ActualWidth || point.Y > MainBorder.ActualHeight)
                {
                    this.Hide();
                }
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (ManagerView.Visibility == Visibility.Visible)
                {
                    ExitManagerMode();
                }
                else if (ExitChatArrow.Visibility == Visibility.Visible)
                {
                    AnimateToSpotlightMode();
                }
                else
                {
                    this.Hide();
                }
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Get the handle for this window
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            
            // Add the hook to listen for Windows messages
            _source?.AddHook(WndProc);

            // Register the hotkey: Alt + Space
            if (!RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, VK_SPACE))
            {
                MessageBox.Show("Hotkey registration failed. Another app is using this combo.", "Error");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWindow();
                handled = true;
            }
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_KEYMENU)
            {
                handled = true; // Prevents the Alt+Space System Menu from stealing focus
            }

            return IntPtr.Zero;
        }

        private void ToggleWindow()
        {
            if (this.IsVisible)
            {
                ManagerView.Visibility = Visibility.Collapsed;
                SpotlightView.Visibility = Visibility.Visible;
                ExitManagerMode();
                ExitChatArrow.Visibility = Visibility.Collapsed;
                this.Hide();
            }
            else
            {
                this.Show();
                this.Activate();
                Mouse.Capture(null);
                
                FocusManager.SetFocusedElement(this, InputBox);
                Keyboard.Focus(InputBox);
                InputBox.Focus();
                UpdateResumeIndicator();
            }
        }

        private void SearchResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is AppSearchResult result)
            {
                LaunchApp(result);
            }
        }

        private void LaunchApp(AppSearchResult selectedItem)
        {
            if (selectedItem.FilePath == "MATH")
            {
                Clipboard.SetText(selectedItem.AppName);
                this.Hide();
                InputBox.Clear();
                return;
            }

            if (selectedItem.FilePath.StartsWith("COMMAND:"))
            {
                if (selectedItem.FilePath == "COMMAND:SHORTCUTS") EnterManagerMode("Shortcuts");
                if (selectedItem.FilePath == "COMMAND:SETTINGS") EnterManagerMode("General");
                if (selectedItem.FilePath == "COMMAND:COMMANDS") EnterManagerMode("Commands");
                if (selectedItem.FilePath == "COMMAND:RESUME")
                {
                    InputBox.Clear();
                    AnimateToChatMode();
                    return;
                }
                SearchResultList.Visibility = Visibility.Collapsed;
                return;
            }
            
            if (selectedItem.FilePath.StartsWith("EXTENSION:"))
            {
                string trigger = selectedItem.FilePath.Substring("EXTENSION:".Length);
                var ext = _loadedExtensions.FirstOrDefault(e => e.TriggerName == trigger);
                if (ext != null)
                {
                    try
                    {
                        string script = ext.PowerShellScript.Replace("\"", "\\\"");
                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"");
                        psi.CreateNoWindow = true; psi.UseShellExecute = false; Process.Start(psi);
                    }
                    catch (Exception ex) { AddChatBubble("System", $"Extension Error: {ex.Message}"); }
                }
                this.Hide();
                InputBox.Clear();
                return;
            }

            if (selectedItem.FilePath.StartsWith("SHORTCUT:"))
            {
                // Auto-fill the shortcut keyword and a space
                InputBox.Text = selectedItem.AppName + " ";
                InputBox.CaretIndex = InputBox.Text.Length;
                InputBox.Focus();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = selectedItem.FilePath, UseShellExecute = true });
            }
            catch (Exception ex) { AddChatBubble("System", $"Error: {ex.Message}"); }

            InputBox.Clear();
            SearchResultList.Visibility = Visibility.Collapsed;
            this.Hide();
        }

        private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Right && ResumeChatIndicator.Visibility == Visibility.Visible)
            {
                ResumeChat();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
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

            if (e.Key == Key.Up)
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
                if (SearchResultList.Visibility == Visibility.Visible && SearchResultList.Items.Count > 0)
                {
                    var selectedItem = (AppSearchResult)(SearchResultList.SelectedItem ?? SearchResultList.Items[0]);
                    LaunchApp(selectedItem);
                    return;
                }

                string userText = InputBox.Text;
                if (string.IsNullOrWhiteSpace(userText)) return;

                // Special Command: Edit Shortcuts
                if (userText.Trim().ToLower() == "shortcuts")
                {
                    EnterManagerMode("Shortcuts");
                    return;
                }
                if (userText.Trim().ToLower() == "settings")
                {
                    EnterManagerMode("General");
                    return;
                }

                // Custom Shortcuts
                var parts = userText.Split(' ', 2);
                var shortcut = _userShortcuts.FirstOrDefault(s => s.Keyword.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (shortcut != null)
                {
                    string query = parts.Length > 1 ? parts[1] : "";
                    string url = shortcut.UrlTemplate.Replace("{query}", Uri.EscapeDataString(query));
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    }
                    catch { }
                    InputBox.Clear();
                    this.Hide();
                    return;
                }

                InputBox.Clear();
                InputBox.IsEnabled = false;

                AnimateToChatMode();
                AddChatBubble("You", userText);

                if (await ProcessLocalCommand(userText))
                {
                    InputBox.IsEnabled = true;
                    InputBox.Focus();
                    return;
                }

                await AskGeminiAsync(userText);
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            UpdateResumeIndicator();

            string query = InputBox.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                HideSearchList();
                return;
            }

            // Math Evaluation
            try
            {
                var mathResult = new DataTable().Compute(query, null);
                if (mathResult != null && !double.IsNaN(Convert.ToDouble(mathResult)))
                {
                    var resultItem = new AppSearchResult 
                    { 
                        AppName = $"= {mathResult}", 
                        FilePath = "MATH", 
                        AppIcon = null 
                    };
                    SearchResultList.ItemsSource = new List<AppSearchResult> { resultItem };
                    ShowSearchList();
                    SearchResultList.SelectedIndex = 0;
                    return; // Skip app search if math is valid
                }
            }
            catch { }

            var results = SearchInstalledApps(query);
            if (results.Any())
            {
                SearchResultList.ItemsSource = results;
                ShowSearchList();
                SearchResultList.SelectedIndex = 0;
            }
            else
            {
                HideSearchList();
            }
        }

        private List<AppSearchResult> SearchInstalledApps(string query)
        {
            var candidates = _appCache.Concat(_shortcutCache).ToList();

            if (_chatHistory.Count > 0)
            {
                candidates.Add(new AppSearchResult
                {
                    AppName = "Resume Chat",
                    FilePath = "COMMAND:RESUME",
                    GeometryIcon = Geometry.Parse("M20,2H4A2,2 0 0,0 2,4V22L6,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2M20,16H6L4,18V4H20")
                });
            }

            return candidates
                .DistinctBy(x => x.AppName)
                .Select(item => new { Item = item, Score = GetScore(item.AppName, query) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.AppName)
                .Take(5)
                .Select(x => x.Item)
                .ToList();
        }

        private void RefreshAppCache()
        {
            _appCache.Clear();

            // Virtual Results
            _appCache.Add(new AppSearchResult 
            { 
                AppName = "Compass Settings", 
                FilePath = "COMMAND:SETTINGS", 
                GeometryIcon = Geometry.Parse("M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z")
            });
            _appCache.Add(new AppSearchResult 
            { 
                AppName = "Manage Shortcuts", 
                FilePath = "COMMAND:SHORTCUTS", 
                GeometryIcon = Geometry.Parse("M4,6H20V8H4V6M4,11H20V13H4V11M4,16H20V18H4V16Z") 
            });
            _appCache.Add(new AppSearchResult 
            { 
                AppName = "Manage Commands", 
                FilePath = "COMMAND:COMMANDS", 
                GeometryIcon = Geometry.Parse("M19,6H5A2,2 0 0,0 3,8V16A2,2 0 0,0 5,18H19A2,2 0 0,0 21,16V8A2,2 0 0,0 19,6M10,15V9L15,12L10,15Z")
            });

            _appCache.AddRange(LoadExtensions());

            void ScanDir(string dir)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        var files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            _appCache.Add(new AppSearchResult { AppName = System.IO.Path.GetFileNameWithoutExtension(f), FilePath = f, AppIcon = GetIcon(f) });
                        }
                    }
                }
                catch { }
            }

            ScanDir(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonPrograms));
            ScanDir(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Programs));
        }

        private void RefreshShortcutCache()
        {
            _shortcutCache = _userShortcuts.Select(s => new AppSearchResult
            {
                AppName = s.Keyword,
                FilePath = "SHORTCUT:" + s.Keyword,
                GeometryIcon = Geometry.Parse("M4,6H20V8H4V6M4,11H20V13H4V11M4,16H20V18H4V16Z")
            }).ToList();
        }

        private void EnsureExtensionsFolderExists()
        {
            if (!Directory.Exists(_extensionsPath))
            {
                Directory.CreateDirectory(_extensionsPath);
            }
        }

        private List<AppSearchResult> LoadExtensions()
        {
            _loadedExtensions.Clear();
            var results = new List<AppSearchResult>();
            if (!Directory.Exists(_extensionsPath)) return results;

            foreach (var file in Directory.GetFiles(_extensionsPath, "*.json"))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(file);
                    var ext = JsonSerializer.Deserialize<CompassExtension>(json);
                    if (ext != null)
                    {
                        _loadedExtensions.Add(ext);
                        results.Add(new AppSearchResult
                        {
                            AppName = ext.TriggerName,
                            FilePath = $"EXTENSION:{ext.TriggerName}",
                            GeometryIcon = Geometry.Parse("M19,6H5A2,2 0 0,0 3,8V16A2,2 0 0,0 5,18H19A2,2 0 0,0 21,16V8A2,2 0 0,0 19,6M10,15V9L15,12L10,15Z")
                        });
                    }
                }
                catch { }
            }
            return results;
        }

        private int GetScore(string text, string query)
        {
            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
            if (text.IndexOf(" " + query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        private System.Windows.Media.ImageSource? GetIcon(string filePath)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (icon == null) return null;
                    return Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> ProcessLocalCommand(string input)
        {
            string lowerInput = input.ToLower().Trim();

            if (lowerInput.StartsWith("create command "))
            {
                var parts = input.Split(' ', 3);
                if (parts.Length == 3)
                {
                    string name = parts[1];
                    string intent = parts[2];
                    await GenerateExtensionAsync(intent, name);
                    return true;
                }
            }

            if (lowerInput == "clear" || lowerInput == "clear chat")
            {
                ChatPanel.Children.Clear();
                _chatHistory.Clear();
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
                    return true;
                }
                catch (Exception ex)
                {
                    AddChatBubble("System", $"Error: {ex.Message}");
                    return true;
                }
            }
            else if (lowerInput == "light mode")
            {
                try
                {
                    const string key = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                    Registry.SetValue(key, "AppsUseLightTheme", 1);
                    Registry.SetValue(key, "SystemUsesLightTheme", 1);
                    AddChatBubble("System", "Switched to Light Mode.");
                    return true;
                }
                catch (Exception ex)
                {
                    AddChatBubble("System", $"Error: {ex.Message}");
                    return true;
                }
            }

            if (lowerInput == "lock screen")
            {
                try
                {
                    Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                    AddChatBubble("System", "Locking screen...");
                    return true;
                }
                catch (Exception ex)
                {
                    AddChatBubble("System", $"Error: {ex.Message}");
                    return true;
                }
            }

            return false;
        }

        private async Task AskGeminiAsync(string prompt)
        {
            try
            {
                // Add the user's message to the history
                _chatHistory.Add(new Content
                {
                    Role = "user",
                    Parts = new List<Part> { new Part { Text = prompt } }
                });

                // Initialize the official client
                var client = new Google.GenAI.Client(apiKey: _appSettings.ApiKey);
                
                // Pass your custom system instructions
                var config = new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = new List<Part> { new Part { Text = _appSettings.SystemPrompt } } }
                };

                // Call the API using your full chat history context
                var response = await client.Models.GenerateContentAsync(
                    model: _appSettings.SelectedModel,
                    contents: _chatHistory,
                    config: config
                );

                string? text = response.Candidates?[0].Content?.Parts?[0].Text;

                if (text != null)
                {
                    _chatHistory.Add(new Content
                    {
                        Role = "model",
                        Parts = new List<Part> { new Part { Text = text } }
                    });
                }
                
                AddChatBubble("Gemini", text ?? "...");
            }
            catch (Exception ex)
            {
                AddChatBubble("System", $"Error: {ex.Message}");
            }
            finally
            {
                InputBox.IsEnabled = true;
                InputBox.Focus();
            }
        }

        private async Task GenerateExtensionAsync(string intent, string name)
        {
            try
            {
                AddChatBubble("System", $"Generating command '{name}'...");
                
                var client = new Google.GenAI.Client(apiKey: _appSettings.ApiKey);
                
                var config = new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = new List<Part> { new Part { Text = "You are a Windows automation expert. The user wants to: " + intent + ". Write a safe, functional PowerShell script to do this. Return ONLY the raw script text. No markdown formatting, no backticks, no explanations." } } }
                };

                var response = await client.Models.GenerateContentAsync(
                    model: _appSettings.SelectedModel,
                    contents: intent, // Single prompt, no history needed
                    config: config
                );

                string? script = response.Candidates?[0].Content?.Parts?[0].Text;

                if (script != null)
                {
                    script = script.Replace("```powershell", "").Replace("```", "").Trim();
                    var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
                    string extJson = JsonSerializer.Serialize(ext, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(System.IO.Path.Combine(_extensionsPath, $"{name}.json"), extJson);
                    
                    RefreshAppCache();
                    AddChatBubble("System", $"Command '{name}' created successfully.");
                }
            }
            catch (Exception ex)
            {
                AddChatBubble("System", $"Failed to generate extension: {ex.Message}");
            }
        }

        private void AddChatBubble(string sender, string text)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(10, 5, 10, 5),
                MaxWidth = 500
            };

            if (sender == "You")
            {
                border.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E"));
                border.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                border.Background = System.Windows.Media.Brushes.Transparent;
                border.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A2A"));
                border.BorderThickness = new Thickness(1);
                border.HorizontalAlignment = HorizontalAlignment.Left;
            }

            var textBox = new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
                FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
                Margin = new Thickness(5),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.IBeam
            };

            border.Child = textBox;
            ChatPanel.Children.Add(border);
            ChatScroll.ScrollToBottom();
        }

        private void AnimateToChatMode()
        {
            HideSearchList();
            ExitChatArrow.Visibility = Visibility.Visible;
            ClearChatBtn.Visibility = Visibility.Visible;
            ChatScroll.Visibility = Visibility.Visible;
            ChatScroll.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
            
            var scaleAnim = new DoubleAnimation(1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ChatScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            UpdateResumeIndicator();
        }

        private void AnimateToSpotlightMode()
        {
            HideSearchList();
            ExitChatArrow.Visibility = Visibility.Collapsed;
            ClearChatBtn.Visibility = Visibility.Collapsed;
            
            var scaleAnim = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            scaleAnim.Completed += (s, e) => 
            {
                ChatScroll.Visibility = Visibility.Collapsed;
                UpdateResumeIndicator();
            };
            ChatScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        private void UpdateResumeIndicator()
        {
            if (ChatScroll.Visibility == Visibility.Visible)
            {
                ResumeChatIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            bool hasHistory = _chatHistory.Count > 0;
            bool isEmpty = string.IsNullOrEmpty(InputBox.Text);
            ResumeChatIndicator.Visibility = (hasHistory && isEmpty) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResumeChat()
        {
            InputBox.Clear();
            AnimateToChatMode();
        }

        private void ResumeChat_Click(object sender, MouseButtonEventArgs e)
        {
            ResumeChat();
        }

        private void ClearChatBtn_Click(object sender, MouseButtonEventArgs e)
        {
            _chatHistory.Clear();
            ChatPanel.Children.Clear();
            AddChatBubble("System", "Chat history cleared.");
            UpdateResumeIndicator();
        }

        private void ExitChatBtn_Click(object sender, MouseButtonEventArgs e)
        {
            AnimateToSpotlightMode();
            ExitChatArrow.Visibility = Visibility.Collapsed;
        }

        private void ShowSearchList()
        {
            if (SearchScale.ScaleY == 1) return;
            SearchResultList.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            SearchScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void HideSearchList()
        {
            if (SearchScale.ScaleY == 0) return;
            var anim = new DoubleAnimation(0, TimeSpan.FromSeconds(0.2))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, e) => SearchResultList.Visibility = Visibility.Collapsed;
            SearchScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up the hotkey registration to prevent memory leaks or system conflicts
            _source?.RemoveHook(WndProc);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            base.OnClosed(e);
        }

        private void LoadShortcuts()
        {
            const string fileName = "shortcuts.json";
            if (System.IO.File.Exists(fileName))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(fileName);
                    _userShortcuts = JsonSerializer.Deserialize<List<CustomShortcut>>(json) ?? new List<CustomShortcut>();
                    RefreshShortcutCache();
                }
                catch { }
            }
            else
            {
                _userShortcuts = new List<CustomShortcut>
                {
                    new CustomShortcut { Keyword = "google", UrlTemplate = "https://www.google.com/search?q={query}" },
                    new CustomShortcut { Keyword = "yt", UrlTemplate = "https://www.youtube.com/results?search_query={query}" }
                };
                try
                {
                    string json = JsonSerializer.Serialize(_userShortcuts, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(fileName, json);
                    RefreshShortcutCache();
                }
                catch { }
            }
        }

        private void SaveShortcuts()
        {
            try
            {
                string json = JsonSerializer.Serialize(_userShortcuts, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText("shortcuts.json", json);
            }
            catch { }
        }

        private void EnterManagerMode(string tabName = "General")
        {
            SpotlightView.Visibility = Visibility.Collapsed;
            ManagerView.Visibility = Visibility.Visible;
            ShortcutsList.ItemsSource = null;
            ShortcutsList.ItemsSource = _userShortcuts;
            
            CommandsList.ItemsSource = null;
            CommandsList.ItemsSource = _loadedExtensions;
            
            ApiKeyBox.Password = _appSettings.ApiKey;
            StartupCheck.IsChecked = _appSettings.LaunchAtStartup;
            OpacitySlider.Value = _appSettings.WindowOpacity;
            SystemPromptBox.Text = _appSettings.SystemPrompt;
            RefreshModelList();

            foreach (TabItem tab in SettingsTabs.Items)
            {
                if ((string)tab.Header == tabName)
                {
                    SettingsTabs.SelectedItem = tab;
                    break;
                }
            }
        }

        private void RefreshModelList()
        {
            ModelComboBox.ItemsSource = null;
            ModelComboBox.ItemsSource = _appSettings.AvailableModels;
            ModelComboBox.SelectedItem = _appSettings.SelectedModel;

            ModelsList.ItemsSource = null;
            ModelsList.ItemsSource = _appSettings.AvailableModels;
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is string model)
            {
                _appSettings.SelectedModel = model;
                SaveSettings();
            }
        }

        private void AddModel_Click(object sender, RoutedEventArgs e)
        {
            string newModel = NewModelBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(newModel) && !_appSettings.AvailableModels.Contains(newModel))
            {
                _appSettings.AvailableModels.Add(newModel);
                RefreshModelList();
                NewModelBox.Clear();
                SaveSettings();
            }
        }

        private void DeleteModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string model)
            {
                _appSettings.AvailableModels.Remove(model);
                if (_appSettings.SelectedModel == model)
                {
                    _appSettings.SelectedModel = _appSettings.AvailableModels.FirstOrDefault() ?? "gemini-2.0-flash";
                }
                RefreshModelList();
                SaveSettings();
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

        private void AddShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NewKeywordBox.Text) && !string.IsNullOrWhiteSpace(NewUrlBox.Text))
            {
                _userShortcuts.Add(new CustomShortcut { Keyword = NewKeywordBox.Text.Trim(), UrlTemplate = NewUrlBox.Text.Trim() });
                NewKeywordBox.Clear();
                NewUrlBox.Clear();
                ShortcutsList.ItemsSource = null;
                ShortcutsList.ItemsSource = _userShortcuts;
                RefreshShortcutCache();
                SaveShortcuts();
            }
        }

        private void DeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CustomShortcut shortcut)
            {
                _userShortcuts.Remove(shortcut);
                ShortcutsList.ItemsSource = null;
                ShortcutsList.ItemsSource = _userShortcuts;
                RefreshShortcutCache();
                SaveShortcuts();
            }
        }

        private async void CreateCommand_Click(object sender, RoutedEventArgs e)
        {
            string name = NewCommandTrigger.Text.Trim();
            string intent = NewCommandIntent.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(intent))
            {
                MessageBox.Show("Please enter a trigger name and description.");
                return;
            }

            var btn = (Button)sender;
            btn.IsEnabled = false;
            btn.Content = "Generating...";

            try
            {
                var client = new Google.GenAI.Client(apiKey: _appSettings.ApiKey);
                var config = new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = new List<Part> { new Part { Text = "You are a Windows automation expert. The user wants to: " + intent + ". Write a safe, functional PowerShell script to do this. Return ONLY the raw script text. No markdown formatting, no backticks, no explanations." } } }
                };

                var response = await client.Models.GenerateContentAsync(
                    model: _appSettings.SelectedModel,
                    contents: intent,
                    config: config
                );

                string? script = response.Candidates?[0].Content?.Parts?[0].Text;

                if (script != null)
                {
                    script = script.Replace("```powershell", "").Replace("```", "").Trim();
                    var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
                    string extJson = JsonSerializer.Serialize(ext, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(System.IO.Path.Combine(_extensionsPath, $"{name}.json"), extJson);
                    
                    RefreshAppCache();
                    CommandsList.ItemsSource = null;
                    CommandsList.ItemsSource = _loadedExtensions;
                    
                    NewCommandTrigger.Clear();
                    NewCommandIntent.Clear();
                    MessageBox.Show($"Command '{name}' created successfully!");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate command: {ex.Message}");
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "Generate Command";
            }
        }

        private void DeleteCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CompassExtension ext)
            {
                try
                {
                    string path = System.IO.Path.Combine(_extensionsPath, $"{ext.TriggerName}.json");
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                    
                    RefreshAppCache();
                    CommandsList.ItemsSource = null;
                    CommandsList.ItemsSource = _loadedExtensions;
                }
                catch (Exception ex) { MessageBox.Show("Error deleting command: " + ex.Message); }
            }
        }

        private void LoadSettings()
        {
            const string fileName = "settings.json";
            if (System.IO.File.Exists(fileName))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(fileName);
                    _appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    if (_appSettings.AvailableModels == null || !_appSettings.AvailableModels.Any())
                    {
                        _appSettings.AvailableModels = new List<string> { "gemini-2.0-flash", "gemini-1.5-pro" };
                    }
                }
                catch { }
            }

            MainBorder.Opacity = _appSettings.WindowOpacity;
        }

        private void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_appSettings, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText("settings.json", json);
            }
            catch { }
        }

        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            _appSettings.ApiKey = ApiKeyBox.Password;
            _appSettings.SystemPrompt = SystemPromptBox.Text;
            SaveSettings();
            MessageBox.Show("Settings saved.", "Compass");
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
                {
                    key?.SetValue("Compass", System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                }
                else
                {
                    key?.DeleteValue("Compass", false);
                }
            }
            catch { }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MainBorder == null || !IsLoaded) return;
            _appSettings.WindowOpacity = OpacitySlider.Value;
            MainBorder.Opacity = _appSettings.WindowOpacity;
            SaveSettings();
        }
    }

    public class AppSearchResult
    {
        public string AppName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public System.Windows.Media.ImageSource? AppIcon { get; set; }
        public System.Windows.Media.Geometry? GeometryIcon { get; set; }
    }

    public class CustomShortcut
    {
        public string Keyword { get; set; } = string.Empty;
        public string UrlTemplate { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        public string ApiKey { get; set; } = "";
        public double WindowOpacity { get; set; } = 1.0;
        public bool LaunchAtStartup { get; set; } = false;
        public string SystemPrompt { get; set; } = "You are a helpful desktop assistant.";
        public string SelectedModel { get; set; } = "gemini-2.0-flash";
        public List<string> AvailableModels { get; set; } = new List<string> { "gemini-2.0-flash", "gemini-1.5-pro" };
    }

    public class CompassExtension
    {
        public string TriggerName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PowerShellScript { get; set; } = string.Empty;
    }
}
