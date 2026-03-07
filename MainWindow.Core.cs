using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Compass.Services;
using Compass.Services.Interfaces;
using Compass.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Compass;

/// <summary>
/// MainWindow - Core functionality, lifecycle, window management, tray
/// </summary>
public partial class MainWindow : Window
{
    // --- Services ---
    private readonly ISettingsService _settingsService;
    private readonly IGeminiService _geminiService;
    private readonly IExtensionService _extService;
    private readonly IAppSearchService _searchService;
    private readonly ISystemCommandService _systemCommandService;
    private readonly IModelRoutingService _routingService;
    private readonly ILogger<MainWindow> _logger;
    private readonly Plugins.PluginHost _pluginHost;
    private readonly ClipboardHistoryService _clipboardHistoryService;
    private readonly RagService _ragService;
    private readonly NotificationService _notificationService;
    private readonly CalendarService _calendarService;
    private readonly MediaSessionService _mediaSessionService;

    // --- ViewModels ---
    private readonly SpotlightViewModel _spotlightVm;
    private readonly ChatViewModel _chatVm;
    private readonly ManagerViewModel _managerVm;

    // --- State ---
    private AppSettings _appSettings = new();
    private List<CustomShortcut> _userShortcuts = new();
    private List<CompassExtension> _extensions = new();
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _isPinned;

    // --- Personalization state ---
    private PersonalizationProposal? _currentPersonalizationProposal;
    private AppSettings? _settingsBackup;
    private bool _isSyncingPersonalization;

    // --- Cancellation & debounce ---
    private CancellationTokenSource? _chatCts;
    private DispatcherTimer? _opacitySaveTimer;

    // --- Image attachment state ---
    private readonly List<(byte[] data, string mimeType, string fileName)> _pendingImages = new();

    // --- Widget state ---
    private readonly IWidgetService _widgetService;
    private readonly WeatherService _weatherService;
    private List<CompassWidget> _widgets = new();
    private readonly Dictionary<string, DispatcherTimer> _widgetTimers = new();

    // --- Search debounce ---
    private CancellationTokenSource? _searchCts;

    // --- Toast state ---
    private DispatcherTimer? _toastDismissTimer;
    private DateTime _lastCpuAlert = DateTime.MinValue;
    private DateTime _lastDiskAlert = DateTime.MinValue;

    // --- Widget drag-and-drop state ---
    private Point _widgetDragStartPoint;
    private Point _widgetDragOffset;
    private bool _isWidgetDragging;
    private Border? _draggedWidgetContainer;
    private WidgetDragAdorner? _dragAdorner;

    // --- Dialog state ---
    private TaskCompletionSource<bool>? _dialogTcs;

    // --- Floating widget windows ---
    private readonly Dictionary<string, Window> _floatingWidgetWindows = new();

    // --- Greetings ---
    private static readonly string[] _morningGreetings = {
        "Good morning, {user}!", "Rise and shine, {user}!", "Morning! What can I help with?",
        "Top of the morning, {user}!", "Ready to start the day, {user}?"
    };
    private static readonly string[] _afternoonGreetings = {
        "Good afternoon, {user}!", "Hey {user}, what's up?", "How's your day going, {user}?",
        "Afternoon, {user}! Need a hand?", "What can I do for you, {user}?"
    };
    private static readonly string[] _eveningGreetings = {
        "Good evening, {user}!", "Evening, {user}! Need anything?", "Winding down, {user}?",
        "Hey {user}, how was your day?", "Evening! What's on your mind?"
    };
    private static readonly string[] _lateNightGreetings = {
        "Burning the midnight oil, {user}?", "Still up, {user}? What do you need?",
        "Late night, {user}? I'm here to help.", "Can't sleep, {user}?"
    };
    private static readonly string[] _generalGreetings = {
        "Hey {user}!", "What's on your mind?", "Hi there! How can I help?",
        "What can I find for you?", "Ready when you are, {user}!"
    };

    // --- Native Windows API Constants ---
    private const int HOTKEY_ID = 9000;
    private const int HOTKEY_ID_COMMANDS = 9001;
    private const int HOTKEY_ID_CLIPBOARD = 9002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;
    private const uint VK_OEM_2 = 0xBF;
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

    private async void FireAndForget(Task task, string context)
    {
        try { await task; }
        catch (Exception ex) { _logger.LogError(ex, "Fire-and-forget failed: {Context}", context); }
    }

    private async Task RefreshExtensionCacheAsync()
    {
        _extensions = _extService.LoadExtensions();
        await _searchService.RefreshCacheAsync(_extensions);
        BuildTrayMenu();
    }

    // --- Constructor ---

    public MainWindow(
        ISettingsService settingsService,
        IGeminiService geminiService,
        IExtensionService extensionService,
        IAppSearchService searchService,
        ISystemCommandService systemCommandService,
        IModelRoutingService routingService,
        IWidgetService widgetService,
        WeatherService weatherService,
        Plugins.PluginHost pluginHost,
        ClipboardHistoryService clipboardHistoryService,
        RagService ragService,
        NotificationService notificationService,
        CalendarService calendarService,
        MediaSessionService mediaSessionService,
        SpotlightViewModel spotlightVm,
        ChatViewModel chatVm,
        ManagerViewModel managerVm,
        ILogger<MainWindow> logger)
    {
        _settingsService = settingsService;
        _geminiService = geminiService;
        _extService = extensionService;
        _searchService = searchService;
        _systemCommandService = systemCommandService;
        _routingService = routingService;
        _widgetService = widgetService;
        _weatherService = weatherService;
        _pluginHost = pluginHost;
        _clipboardHistoryService = clipboardHistoryService;
        _ragService = ragService;
        _notificationService = notificationService;
        _calendarService = calendarService;
        _mediaSessionService = mediaSessionService;
        _spotlightVm = spotlightVm;
        _chatVm = chatVm;
        _managerVm = managerVm;
        _logger = logger;

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
            _widgetService.EnsureWidgetsFolderExists();
            _widgets = _widgetService.GetAllWidgets();

            // Apply saved widget sizes
            foreach (var widget in _widgets)
            {
                if (_appSettings.WidgetSizes.TryGetValue(widget.Id, out var size))
                {
                    widget.WidgetSize = size;
                }
            }

            FireAndForget(InitializeCacheAsync(), "InitializeCacheAsync");

            this.SizeChanged += MainWindow_SizeChanged;
            this.Deactivated += MainWindow_Deactivated;
            this.MouseDown += Window_MouseDown;
            this.IsVisibleChanged += MainWindow_IsVisibleChanged;

            // Widget panel drag handling
            WidgetPanel.AllowDrop = true;
            WidgetPanel.DragOver += (s, e) =>
            {
                if (_dragAdorner != null && e.Data.GetDataPresent("WidgetDrag"))
                {
                    _dragAdorner.UpdatePosition(e.GetPosition(WidgetPanel));
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };

            WidgetScroll.DragOver += (s, e) =>
            {
                if (_dragAdorner != null && e.Data.GetDataPresent("WidgetDrag"))
                {
                    _dragAdorner.UpdatePosition(e.GetPosition(WidgetPanel));
                    e.Effects = DragDropEffects.Move;
                }
            };
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Initialization failed: " + ex.Message + "\n" + ex.StackTrace, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _logger.LogCritical(ex, "Initialization failed");
            throw;
        }
    }

    // --- Window lifecycle ---

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
            PositionOnActiveMonitor();
    }

    private void PositionOnActiveMonitor()
    {
        var cursorPos = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        var wa = screen.WorkingArea;

        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = 1.0, dpiScaleY = 1.0;
        if (source?.CompositionTarget != null)
        {
            var transform = source.CompositionTarget.TransformFromDevice;
            dpiScaleX = transform.M11;
            dpiScaleY = transform.M22;
        }

        double waLeft = wa.Left * dpiScaleX;
        double waTop = wa.Top * dpiScaleY;
        double waWidth = wa.Width * dpiScaleX;
        double waHeight = wa.Height * dpiScaleY;

        this.Left = waLeft + (waWidth - this.ActualWidth) / 2;
        if (_appSettings.WindowVerticalPosition == "TopThird")
            this.Top = waTop + (waHeight / 3.0) - (this.ActualHeight / 2);
        else
            this.Top = waTop + (waHeight - this.ActualHeight) / 2;
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue == false && ManagerView.Visibility == Visibility.Visible)
            ExitManagerMode();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_isPinned) this.Hide();
    }

    private void PinToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _isPinned = !_isPinned;
        ResetPinVisuals();
    }

    private void ResetPinVisuals()
    {
        if (PinIcon != null) PinIcon.RenderTransform = new RotateTransform(_isPinned ? 0 : 45);
        if (PinIconManager != null) PinIconManager.RenderTransform = new RotateTransform(_isPinned ? 0 : 45);
        if (PinBtn != null) PinBtn.Opacity = _isPinned ? 1.0 : 0.5;
        if (PinBtnManager != null) PinBtnManager.Opacity = _isPinned ? 1.0 : 0.5;
    }

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
            else if (!string.IsNullOrEmpty(InputBox.Text))
            {
                InputBox.Clear();
                SearchResultList.Visibility = Visibility.Collapsed;
            }
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

        RegisterHotKey(_windowHandle, HOTKEY_ID_COMMANDS, MOD_ALT | MOD_NOREPEAT, VK_OEM_2);

        const uint MOD_CTRL = 0x0002;
        const uint MOD_SHIFT = 0x0004;
        const uint VK_V = 0x56;
        RegisterHotKey(_windowHandle, HOTKEY_ID_CLIPBOARD, MOD_CTRL | MOD_SHIFT | MOD_NOREPEAT, VK_V);

        _notificationService.OnToast += (title, message) => Dispatcher.Invoke(() => ShowToast(title, message));

        InitializeTrayIcon();

        if (!_appSettings.HasSeenWelcome)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                SpotlightView.Visibility = Visibility.Collapsed;
                ManagerView.Visibility = Visibility.Collapsed;
                MainBorder.Background = System.Windows.Media.Brushes.Transparent;
                MainBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                MainBorder.Effect = null;
                this.Show();
                PositionOnActiveMonitor();
                this.Activate();
                WelcomeOverlay.Visibility = Visibility.Visible;
            });
        }

        RestoreFloatingWidgets();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID)
            {
                ToggleWindow();
                handled = true;
            }
            else if (id == HOTKEY_ID_COMMANDS)
            {
                ToggleWindowCommandMode();
                handled = true;
            }
            else if (id == HOTKEY_ID_CLIPBOARD)
            {
                ShowClipboardPanel();
                handled = true;
            }
        }
        if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_KEYMENU)
            handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        UnregisterHotKey(_windowHandle, HOTKEY_ID_COMMANDS);
        UnregisterHotKey(_windowHandle, HOTKEY_ID_CLIPBOARD);

        _opacitySaveTimer?.Stop();
        _toastDismissTimer?.Stop();
        StopWidgetTimers();

        _notifyIcon?.Dispose();
        base.OnClosed(e);
    }

    // --- Toggle / Tray ---

    private void ToggleWindow()
    {
        if (this.IsVisible)
        {
            _chatCts?.Cancel();
            _isPinned = false;
            ResetPinVisuals();
            ManagerView.Visibility = Visibility.Collapsed;
            SpotlightView.Visibility = Visibility.Visible;
            ExitManagerMode();
            ExitChatArrow.Visibility = Visibility.Collapsed;
            this.Hide();
        }
        else
        {
            this.Show();
            PositionOnActiveMonitor();
            this.Activate();
            Mouse.Capture(null);
            FocusManager.SetFocusedElement(this, InputBox);
            Keyboard.Focus(InputBox);
            InputBox.Focus();
            UpdateResumeIndicator();
            if (_appSettings.RandomGreetingsEnabled)
                PlaceholderText.Text = GetRandomGreeting();
            if (string.IsNullOrEmpty(InputBox.Text) && ChatScroll.Visibility != Visibility.Visible)
                ShowWidgetPanel();
        }
    }

    private string GetRandomGreeting()
    {
        int hour = DateTime.Now.Hour;
        string[] pool;
        bool useGeneral = Random.Shared.Next(3) == 0;
        if (useGeneral)
        {
            pool = _generalGreetings;
        }
        else if (hour >= 5 && hour < 12)
            pool = _morningGreetings;
        else if (hour >= 12 && hour < 17)
            pool = _afternoonGreetings;
        else if (hour >= 17 && hour < 24)
            pool = _eveningGreetings;
        else
            pool = _lateNightGreetings;

        string greeting = pool[Random.Shared.Next(pool.Length)];
        string userName = Environment.UserName;
        if (!string.IsNullOrEmpty(userName))
            userName = char.ToUpper(userName[0]) + userName.Substring(1).ToLower();
        return greeting.Replace("{user}", userName);
    }

    private void ToggleWindowCommandMode()
    {
        if (this.IsVisible)
        {
            _chatCts?.Cancel();
            _isPinned = false;
            ResetPinVisuals();
            ManagerView.Visibility = Visibility.Collapsed;
            SpotlightView.Visibility = Visibility.Visible;
            ExitManagerMode();
            ExitChatArrow.Visibility = Visibility.Collapsed;
            this.Hide();
        }
        else
        {
            this.Show();
            PositionOnActiveMonitor();
            this.Activate();
            Mouse.Capture(null);
            FocusManager.SetFocusedElement(this, InputBox);
            Keyboard.Focus(InputBox);
            InputBox.Focus();
            InputBox.Text = "/";
            InputBox.CaretIndex = 1;
            UpdateResumeIndicator();
        }
    }

    private static System.Drawing.Icon CreateCompassIcon()
    {
        const int size = 32;
        using var bmp = new System.Drawing.Bitmap(size, size);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        float cx = size / 2f, cy = size / 2f, r = size / 2f - 1.5f;

        using var ringPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(76, 194, 255), 2f);
        g.DrawEllipse(ringPen, cx - r, cy - r, r * 2, r * 2);

        var north = new System.Drawing.PointF[] {
            new(cx, cy - r + 4f),
            new(cx - 4f, cy),
            new(cx + 4f, cy)
        };
        using var northBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(76, 194, 255));
        g.FillPolygon(northBrush, north);

        var south = new System.Drawing.PointF[] {
            new(cx, cy + r - 4f),
            new(cx - 4f, cy),
            new(cx + 4f, cy)
        };
        using var southBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(140, 140, 140));
        g.FillPolygon(southBrush, south);

        g.FillEllipse(northBrush, cx - 2.5f, cy - 2.5f, 5f, 5f);

        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon { Text = "Compass" };
        try
        {
            _notifyIcon.Icon = CreateCompassIcon();
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
                var capturedExt = ext;
                commandsMenu.DropDownItems.Add(ext.TriggerName, null, (s, e) =>
                {
                    try
                    {
                        string output = _extService.ExecuteExtension(capturedExt);
                        _logger.LogInformation("Extension '{TriggerName}': {Output}", capturedExt.TriggerName, output);
                    }
                    catch (Exception ex) { MessageBox.Show($"Extension '{capturedExt.TriggerName}' failed: {ex.Message}", "Extension Error", MessageBoxButton.OK, MessageBoxImage.Error); }
                });
            }
            contextMenu.Items.Add(commandsMenu);
        }

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) =>
        {
            _isExiting = true;
            SaveSettings();
            foreach (var fw in _floatingWidgetWindows.Values.ToList())
                fw.Close();
            _floatingWidgetWindows.Clear();
            Application.Current.Shutdown();
        });

        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private async Task InitializeCacheAsync()
    {
        await _searchService.RefreshCacheAsync(_extensions);
        BuildTrayMenu();
    }
}
