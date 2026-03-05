using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Compass.Services;
using Microsoft.Win32;

namespace Compass
{
    public partial class MainWindow : Window
    {
        // --- Services ---
        private readonly SettingsService _settingsService = new();
        private readonly GeminiService _geminiService = new();
        private readonly ExtensionService _extService = new();
        private readonly AppSearchService _searchService = new();

        // --- State ---
        private AppSettings _appSettings = new();
        private List<CustomShortcut> _userShortcuts = new();
        private List<CompassExtension> _extensions = new();
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExiting;

        // --- Personalization state ---
        private PersonalizationProposal? _currentPersonalizationProposal;
        private AppSettings? _settingsBackup;

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

        // --- Convenience wrappers ---
        private void SaveSettings() => _settingsService.SaveSettings(_appSettings);
        private void SaveShortcuts() => _settingsService.SaveShortcuts(_userShortcuts);

        private async Task RefreshExtensionCacheAsync()
        {
            _extensions = _extService.LoadExtensions();
            await _searchService.RefreshCacheAsync(_extensions);
            BuildTrayMenu();
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _extService.EnsureExtensionsFolderExists();
                _appSettings = _settingsService.LoadSettings();
                ApplyPersonalizationSettings();
                OpacitySlider.Value = _appSettings.WindowOpacity;
                _userShortcuts = _settingsService.LoadShortcuts();
                _searchService.RefreshShortcutCache(_userShortcuts);
                _extensions = _extService.LoadExtensions();

                // Fire-and-forget: scan apps on background thread
                _ = InitializeCacheAsync();

                this.SizeChanged += MainWindow_SizeChanged;
                this.Deactivated += MainWindow_Deactivated;
                this.MouseDown += Window_MouseDown;
                this.IsVisibleChanged += MainWindow_IsVisibleChanged;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Initialization failed: " + ex.Message + "\n" + ex.StackTrace, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.Error.WriteLine(ex);
                throw;
            }
        }

        private async Task InitializeCacheAsync()
        {
            await _searchService.RefreshCacheAsync(_extensions);
            BuildTrayMenu();
        }

        // ---------------------------------------------------------------------------
        // Window lifecycle
        // ---------------------------------------------------------------------------

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
            {
                this.Top = (SystemParameters.WorkArea.Height - this.ActualHeight) / 2;
                this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == false && ManagerView.Visibility == Visibility.Visible)
                ExitManagerMode();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e) => this.Hide();

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var point = e.GetPosition(MainBorder);
                if (point.X < 0 || point.Y < 0 || point.X > MainBorder.ActualWidth || point.Y > MainBorder.ActualHeight)
                    this.Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (ManagerView.Visibility == Visibility.Visible)
                    ExitManagerMode();
                else if (ExitChatArrow.Visibility == Visibility.Visible)
                    AnimateToSpotlightMode();
                else
                    this.Hide();
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(WndProc);

            if (!RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, VK_SPACE))
                MessageBox.Show("Hotkey registration failed. You might have started Compass already!", "Compass", MessageBoxButton.OK, MessageBoxImage.Warning);

            InitializeTrayIcon();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWindow();
                handled = true;
            }
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_KEYMENU)
                handled = true; // Prevents Alt+Space opening the system menu
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            _source?.RemoveHook(WndProc);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------------------
        // Toggle / Tray
        // ---------------------------------------------------------------------------

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

        private void InitializeTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon { Text = "Compass" };
            try
            {
                var mainModule = Process.GetCurrentProcess().MainModule;
                _notifyIcon.Icon = mainModule != null
                    ? System.Drawing.Icon.ExtractAssociatedIcon(mainModule.FileName)
                    : System.Drawing.SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += (sender, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left) ToggleWindow();
            };
            BuildTrayMenu();
        }

        private void BuildTrayMenu()
        {
            if (_notifyIcon == null) return;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Compass", null, (s, e) => ToggleWindow());

            if (_extensions.Any())
            {
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                var commandsMenu = new System.Windows.Forms.ToolStripMenuItem("Commands");
                foreach (var ext in _extensions.OrderBy(x => x.TriggerName))
                {
                    var capturedExt = ext; // capture for closure
                    commandsMenu.DropDownItems.Add(ext.TriggerName, null, (s, e) =>
                    {
                        try { _extService.ExecuteExtension(capturedExt); }
                        catch (Exception ex) { Debug.WriteLine($"[Compass] TrayExecuteExtension: {ex.Message}"); }
                    });
                }
                contextMenu.Items.Add(commandsMenu);
            }

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) =>
            {
                _isExiting = true;
                Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        // ---------------------------------------------------------------------------
        // Input handling
        // ---------------------------------------------------------------------------

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
                    catch (Exception ex) { Debug.WriteLine($"[Compass] OpenShortcut: {ex.Message}"); }
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

            // Math evaluation
            try
            {
                var mathResult = new DataTable().Compute(query, null);
                if (mathResult != null && !double.IsNaN(Convert.ToDouble(mathResult)))
                {
                    SearchResultList.ItemsSource = new List<AppSearchResult>
                    {
                        new AppSearchResult { AppName = $"= {mathResult}", FilePath = "MATH" }
                    };
                    ShowSearchList();
                    SearchResultList.SelectedIndex = 0;
                    return;
                }
            }
            catch { /* not a math expression */ }

            var results = _searchService.Search(query, _geminiService.HasHistory);
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

        // ---------------------------------------------------------------------------
        // App launching
        // ---------------------------------------------------------------------------

        private void SearchResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is AppSearchResult result)
                LaunchApp(result);
        }

        private void LaunchApp(AppSearchResult selectedItem)
        {
            if (selectedItem.FilePath == "MATH")
            {
                System.Windows.Clipboard.SetText(selectedItem.AppName);
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
                var ext = _extensions.FirstOrDefault(e => e.TriggerName == trigger);
                if (ext != null)
                {
                    try { _extService.ExecuteExtension(ext); }
                    catch (Exception ex) { AddChatBubble("System", $"Extension Error: {ex.Message}"); }
                }
                this.Hide();
                InputBox.Clear();
                return;
            }

            if (selectedItem.FilePath.StartsWith("SHORTCUT:"))
            {
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

            return false;
        }

        // ---------------------------------------------------------------------------
        // Gemini / AI
        // ---------------------------------------------------------------------------

        private async Task AskGeminiAsync(string prompt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
                {
                    AddChatBubble("System", "API key is not set. Please enter a valid Gemini API key in settings.");
                    return;
                }
                string response = await _geminiService.AskAsync(prompt, _appSettings);
                AddChatBubble("Gemini", response);
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

                if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
                {
                    AddChatBubble("System", "API key is not set. Please enter a valid Gemini API key in settings.");
                    return;
                }

                string? script = await _geminiService.GeneratePowerShellScriptAsync(intent, _appSettings);
                if (script != null)
                {
                    var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
                    _extService.SaveExtension(ext);
                    await RefreshExtensionCacheAsync();
                    AddChatBubble("System", $"Command '{name}' created successfully.");
                }
            }
            catch (Exception ex)
            {
                AddChatBubble("System", $"Failed to generate extension: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------------------
        // Chat UI
        // ---------------------------------------------------------------------------

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
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
                border.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                border.Background = Brushes.Transparent;
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
                border.BorderThickness = new Thickness(1);
                border.HorizontalAlignment = HorizontalAlignment.Left;
            }

            var textBox = new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI Variable Text"),
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                Margin = new Thickness(5),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
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
            bool hasHistory = _geminiService.HasHistory;
            bool isEmpty = string.IsNullOrEmpty(InputBox.Text);
            ResumeChatIndicator.Visibility = (hasHistory && isEmpty) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResumeChat()
        {
            InputBox.Clear();
            AnimateToChatMode();
        }

        private void ResumeChat_Click(object sender, MouseButtonEventArgs e) => ResumeChat();

        private void ClearChatBtn_Click(object sender, MouseButtonEventArgs e)
        {
            _geminiService.ClearHistory();
            ChatPanel.Children.Clear();
            AddChatBubble("System", "Chat history cleared.");
            UpdateResumeIndicator();
        }

        private void ExitChatBtn_Click(object sender, MouseButtonEventArgs e)
        {
            AnimateToSpotlightMode();
            ExitChatArrow.Visibility = Visibility.Collapsed;
        }

        // ---------------------------------------------------------------------------
        // Search list animations
        // ---------------------------------------------------------------------------

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

        // Scroll bar hover handlers
        private void ScrollBar_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Grid grid)
            {
                var thumb = FindVisualChild<Thumb>(grid);
                if (thumb != null) thumb.Opacity = 1;
            }
        }

        private void ScrollBar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Grid grid)
            {
                var thumb = FindVisualChild<Thumb>(grid);
                if (thumb != null) thumb.Opacity = 0;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

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
            ApiKeyBox.Password = _appSettings.ApiKey;
            StartupCheck.IsChecked = _appSettings.LaunchAtStartup;
            OpacitySlider.Value = _appSettings.WindowOpacity;
            SystemPromptBox.Text = _appSettings.SystemPrompt;
            RefreshModelList();
            _ = FetchAvailableModelsAsync();

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

        private async Task FetchAvailableModelsAsync()
        {
            var models = await _geminiService.FetchAvailableModelsAsync(_appSettings);
            if (models.Count > 0)
            {
                _appSettings.AvailableModels = models;
                RefreshModelList();
                SaveSettings();
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
                    MessageBox.Show("Please configure your API Key in the General settings first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string? script = await _geminiService.GeneratePowerShellScriptAsync(intent, _appSettings);
                if (script != null)
                {
                    var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
                    _extService.SaveExtension(ext);
                    await RefreshExtensionCacheAsync();
                    CommandsList.ItemsSource = null;
                    CommandsList.ItemsSource = _extensions;
                    NewCommandTrigger.Clear();
                    NewCommandIntent.Clear();
                    btn.Content = "Created!";
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                btn.Content = "Error!";
                Debug.WriteLine($"[Compass] CreateCommand: {ex.Message}");
                MessageBox.Show($"Failed to generate command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Debug.WriteLine($"[Compass] StartupRegistry: {ex.Message}");
            }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MainBorder == null || !IsLoaded) return;
            _appSettings.WindowOpacity = OpacitySlider.Value;
            MainBorder.Opacity = _appSettings.WindowOpacity;
            SaveSettings();
        }

        // ---------------------------------------------------------------------------
        // Personalization
        // ---------------------------------------------------------------------------

        private void ApplyPersonalizationSettings()
        {
            try
            {
                MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_appSettings.PrimaryColor));
                MainBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_appSettings.BorderColor));
                MainBorder.CornerRadius = new CornerRadius(_appSettings.BorderRadius);

                if (_appSettings.WindowWidth > 0) this.Width = _appSettings.WindowWidth;

                if (_appSettings.WindowHeight > 0)
                {
                    this.SizeToContent = SizeToContent.Manual;
                    this.Height = _appSettings.WindowHeight;
                }
                else
                {
                    this.SizeToContent = SizeToContent.Height;
                }

                this.FontFamily = new FontFamily(_appSettings.FontFamily);
                this.FontSize = _appSettings.FontSize;
                PlaceholderText.Text = _appSettings.CompassBoxDefaultText;
                UpdateCurrentPersonalizationDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Compass] ApplyPersonalization: {ex.Message}");
            }
        }

        private void UpdateCurrentPersonalizationDisplay()
        {
            var display = $@"Default Text: ""{_appSettings.CompassBoxDefaultText}""
Primary Color: {_appSettings.PrimaryColor}
Accent Color: {_appSettings.AccentColor}
Window Width: {_appSettings.WindowWidth}
Window Height: {(_appSettings.WindowHeight > 0 ? _appSettings.WindowHeight.ToString() : "Auto")}
Font Family: {_appSettings.FontFamily}
Font Size: {_appSettings.FontSize}
Animations Enabled: {_appSettings.AnimationsEnabled}
Border Color: {_appSettings.BorderColor}
Border Radius: {_appSettings.BorderRadius}";
            CurrentPersonalizationSettings.Text = display;
        }

        private void BackupCurrentSettings()
        {
            if (_settingsBackup != null) return; // Don't overwrite a backup with temporary state
            _settingsBackup = new AppSettings
            {
                CompassBoxDefaultText = _appSettings.CompassBoxDefaultText,
                PrimaryColor = _appSettings.PrimaryColor,
                AccentColor = _appSettings.AccentColor,
                WindowWidth = _appSettings.WindowWidth,
                WindowHeight = _appSettings.WindowHeight,
                FontFamily = _appSettings.FontFamily,
                FontSize = _appSettings.FontSize,
                AnimationsEnabled = _appSettings.AnimationsEnabled,
                BorderColor = _appSettings.BorderColor,
                BorderRadius = _appSettings.BorderRadius
            };
        }

        private void RestoreSettingsBackup()
        {
            if (_settingsBackup == null) return;
            _appSettings.CompassBoxDefaultText = _settingsBackup.CompassBoxDefaultText;
            _appSettings.PrimaryColor = _settingsBackup.PrimaryColor;
            _appSettings.AccentColor = _settingsBackup.AccentColor;
            _appSettings.WindowWidth = _settingsBackup.WindowWidth;
            _appSettings.WindowHeight = _settingsBackup.WindowHeight;
            _appSettings.FontFamily = _settingsBackup.FontFamily;
            _appSettings.FontSize = _settingsBackup.FontSize;
            _appSettings.AnimationsEnabled = _settingsBackup.AnimationsEnabled;
            _appSettings.BorderColor = _settingsBackup.BorderColor;
            _appSettings.BorderRadius = _settingsBackup.BorderRadius;
            _settingsBackup = null;
            ApplyPersonalizationSettings();
        }

        private void QuickStyle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string styleDescription)
                PersonalizationInputBox.Text = styleDescription;
        }

        private void ResetPersonalization_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to reset Compass to default appearance?", "Reset Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var defaults = new AppSettings();
                _appSettings.CompassBoxDefaultText = defaults.CompassBoxDefaultText;
                _appSettings.PrimaryColor = defaults.PrimaryColor;
                _appSettings.AccentColor = defaults.AccentColor;
                _appSettings.WindowWidth = defaults.WindowWidth;
                _appSettings.WindowHeight = defaults.WindowHeight;
                _appSettings.FontFamily = defaults.FontFamily;
                _appSettings.FontSize = defaults.FontSize;
                _appSettings.AnimationsEnabled = defaults.AnimationsEnabled;
                _appSettings.BorderColor = defaults.BorderColor;
                _appSettings.BorderRadius = defaults.BorderRadius;
                _settingsBackup = null;
                PreviewSection.Visibility = Visibility.Collapsed;
                _currentPersonalizationProposal = null;
                PersonalizationInputBox.Clear();
                SaveSettings();
                ApplyPersonalizationSettings();
                MessageBox.Show("Compass appearance has been reset to defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void GeneratePersonalizationPreview_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PersonalizationInputBox.Text))
            {
                MessageBox.Show("Please describe how you want Compass to look.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
            {
                MessageBox.Show("Please configure your API Key in the General settings first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "Generating..."; }

            try
            {
                string systemPrompt = PersonalizationManager.GetPersonalizationSystemPrompt();
                string userRequest = PersonalizationInputBox.Text;

                // Use a fresh single-turn request (not chat history)
                var tempSettings = new AppSettings
                {
                    ApiKey = _appSettings.ApiKey,
                    SelectedModel = _appSettings.SelectedModel,
                    SystemPrompt = systemPrompt
                };
                var singleTurnService = new GeminiService();

                string jsonText = await singleTurnService.AskAsync(userRequest, tempSettings);

                // Strip markdown fences if present
                if (jsonText.Contains("```"))
                {
                    int start = jsonText.IndexOf("{");
                    int end = jsonText.LastIndexOf("}");
                    if (start >= 0 && end >= 0)
                        jsonText = jsonText.Substring(start, end - start + 1);
                }

                _currentPersonalizationProposal = PersonalizationManager.ParseResponse(jsonText);

                if (_currentPersonalizationProposal == null ||
                    (string.IsNullOrEmpty(_currentPersonalizationProposal.CompassBoxDefaultText) &&
                     string.IsNullOrEmpty(_currentPersonalizationProposal.PrimaryColor) &&
                     string.IsNullOrEmpty(_currentPersonalizationProposal.AccentColor) &&
                     _currentPersonalizationProposal.WindowWidth == null &&
                     _currentPersonalizationProposal.FontFamily == null))
                {
                    MessageBox.Show("Could not understand your request. Please describe visual changes more clearly.", "Invalid Request", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                BackupCurrentSettings();
                ApplyProposalTemporarily(_currentPersonalizationProposal);
                ShowPersonalizationPreview(_currentPersonalizationProposal);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "Generate & Preview"; }
            }
        }

        private void ApplyProposalTemporarily(PersonalizationProposal proposal)
        {
            if (!string.IsNullOrEmpty(proposal.CompassBoxDefaultText))
                _appSettings.CompassBoxDefaultText = proposal.CompassBoxDefaultText;
            if (!string.IsNullOrEmpty(proposal.PrimaryColor))
                _appSettings.PrimaryColor = proposal.PrimaryColor;
            if (!string.IsNullOrEmpty(proposal.AccentColor))
                _appSettings.AccentColor = proposal.AccentColor;
            if (proposal.WindowWidth.HasValue && proposal.WindowWidth > 0)
                _appSettings.WindowWidth = proposal.WindowWidth.Value;
            if (proposal.WindowHeight.HasValue && proposal.WindowHeight > 0)
                _appSettings.WindowHeight = proposal.WindowHeight.Value;
            if (!string.IsNullOrEmpty(proposal.FontFamily))
                _appSettings.FontFamily = proposal.FontFamily;
            if (proposal.FontSize.HasValue && proposal.FontSize > 0)
                _appSettings.FontSize = proposal.FontSize.Value;
            if (proposal.AnimationsEnabled.HasValue)
                _appSettings.AnimationsEnabled = proposal.AnimationsEnabled.Value;
            if (!string.IsNullOrEmpty(proposal.BorderColor))
                _appSettings.BorderColor = proposal.BorderColor;
            if (proposal.BorderRadius.HasValue && proposal.BorderRadius >= 0)
                _appSettings.BorderRadius = proposal.BorderRadius.Value;
            ApplyPersonalizationSettings();
        }

        private void ShowPersonalizationPreview(PersonalizationProposal proposal)
        {
            var changesList = new List<string>();
            if (!string.IsNullOrEmpty(proposal.CompassBoxDefaultText))
                changesList.Add($"• Default text: \"{proposal.CompassBoxDefaultText}\"");
            if (!string.IsNullOrEmpty(proposal.PrimaryColor))
                changesList.Add($"• Primary color: {proposal.PrimaryColor}");
            if (!string.IsNullOrEmpty(proposal.AccentColor))
                changesList.Add($"• Accent color: {proposal.AccentColor}");
            if (proposal.WindowWidth.HasValue)
                changesList.Add($"• Window width: {proposal.WindowWidth}");
            if (!string.IsNullOrEmpty(proposal.FontFamily))
                changesList.Add($"• Font: {proposal.FontFamily}");
            if (proposal.FontSize.HasValue)
                changesList.Add($"• Font size: {proposal.FontSize}");
            if (proposal.AnimationsEnabled.HasValue)
                changesList.Add($"• Animations: {(proposal.AnimationsEnabled.Value ? "Enabled" : "Disabled")}");
            if (!string.IsNullOrEmpty(proposal.BorderColor))
                changesList.Add($"• Border color: {proposal.BorderColor}");
            if (proposal.BorderRadius.HasValue)
                changesList.Add($"• Corner radius: {proposal.BorderRadius}");
            PreviewChangesText.Text = string.Join("\n", changesList);
            PreviewSection.Visibility = Visibility.Visible;
        }

        private void AcceptPersonalization_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPersonalizationProposal == null) return;
            SaveSettings();
            _settingsBackup = null;
            PreviewSection.Visibility = Visibility.Collapsed;
            PersonalizationInputBox.Clear();
            _currentPersonalizationProposal = null;
            MessageBox.Show("Personalization changes applied successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RejectPersonalization_Click(object sender, RoutedEventArgs e)
        {
            RestoreSettingsBackup();
            PreviewSection.Visibility = Visibility.Collapsed;
            _currentPersonalizationProposal = null;
        }
    }
}
