namespace Compass;

public class AppSettings
{
    public string ApiKey { get; set; } = "";
    public double WindowOpacity { get; set; } = 1.0;
    public bool LaunchAtStartup { get; set; } = false;
    public string SystemPrompt { get; set; } = "You are Compass, a friendly and cheerful Windows desktop assistant. Be conversational, warm, and concise. Match the user's energy — if they say \"ping!\", reply \"pong!\". If they're casual, be casual back. For general conversation, questions, or tasks, just respond naturally.";
    public string SelectedModel { get; set; } = "gemini-2.5-flash";
    public List<string> AvailableModels { get; set; } = new List<string> { "gemini-2.5-flash" };

    // Smart model routing
    public bool SmartRoutingEnabled { get; set; } = false;
    public string FastModel { get; set; } = "gemini-2.5-flash-lite";
    public string PowerModel { get; set; } = "gemini-2.5-flash";
    public string ImageGenerationModel { get; set; } = "gemini-2.5-flash-image";

    // Multi-provider AI (Phase 6)
    public string ActiveProvider { get; set; } = "Gemini";
    public Dictionary<string, ProviderConfig> ProviderConfigs { get; set; } = new();

    // File search (Phase 8)
    public bool FileSearchEnabled { get; set; } = false;
    public List<string> FileSearchDirectories { get; set; } = new();

    // Clipboard history (Phase 10)
    public bool ClipboardHistoryEnabled { get; set; } = true;

    // Plugin toggles (Phase 16)
    public List<string> DisabledPlugins { get; set; } = new();

    // Personalization settings
    public string CompassBoxDefaultText { get; set; } = "Ask Gemini or search apps...";
    public string PrimaryColor { get; set; } = "#1C1C1C";
    public string AccentColor { get; set; } = "#4CC2FF";
    public string SecondaryColor { get; set; } = ""; // empty = auto-derived from primary
    public string TextColor { get; set; } = ""; // empty = auto-derived from primary luminance
    public double WindowWidth { get; set; } = 700;
    public string FontFamily { get; set; } = "Segoe UI Variable Display, Segoe UI";
    public double FontSize { get; set; } = 14;
    public bool AnimationsEnabled { get; set; } = true;
    public string BorderColor { get; set; } = "#2D2D2D";
    public double BorderRadius { get; set; } = 12;
    public string SelectedTheme { get; set; } = "Dark";
    public bool CompactMode { get; set; } = false;
    public string WindowVerticalPosition { get; set; } = "Center"; // "Center" or "TopThird"
    public string ChatBubbleStyle { get; set; } = "Rounded"; // "Rounded" or "Square"

    // Advanced personalization - Background
    public string BackgroundType { get; set; } = "Solid"; // "Solid", "LinearGradient", "RadialGradient"
    public string GradientStartColor { get; set; } = "";
    public string GradientEndColor { get; set; } = "";
    public double GradientAngle { get; set; } = 135.0;

    // Advanced personalization - Background Image
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundImageOpacity { get; set; } = 0.6;

    // Advanced personalization - Window
    public double BorderThickness { get; set; } = 1.0;

    // File content search
    public bool FileContentSearchEnabled { get; set; } = false;
    public List<string> FileContentSearchExtensions { get; set; } = new() { ".txt", ".cs", ".json", ".xml", ".md", ".yaml", ".yml", ".py", ".js", ".ts" };

    // Bookmark search
    public bool BookmarkSearchEnabled { get; set; } = true;

    // RAG
    public bool RagEnabled { get; set; } = false;
    public List<string> RagDirectories { get; set; } = new();

    // Notifications
    public bool NotificationsEnabled { get; set; } = true;
    public double CpuAlertThreshold { get; set; } = 90;
    public double DiskAlertThreshold { get; set; } = 90;

    // Welcome & Greetings
    public bool HasSeenWelcome { get; set; } = false;
    public bool RandomGreetingsEnabled { get; set; } = true;

    // Widgets
    public bool WidgetsEnabled { get; set; } = true;
    public List<string> PinnedWidgetIds { get; set; } = new();
    public Dictionary<string, FloatingWidgetPosition> FloatingWidgets { get; set; } = new();
    public Dictionary<string, string> WidgetSizes { get; set; } = new(); // widgetId -> "1x1" or "2x1"
    public List<string> EnabledWidgetIds { get; set; } = new() { "builtin-clock", "builtin-weather", "builtin-systemstats", "builtin-calendar", "builtin-media", "builtin-notes" };
    public List<string> WidgetOrder { get; set; } = new() { "builtin-clock", "builtin-weather", "builtin-systemstats", "builtin-calendar", "builtin-media", "builtin-notes" };
    public double WeatherLatitude { get; set; } = 0;
    public double WeatherLongitude { get; set; } = 0;
    public string WeatherLocationName { get; set; } = "";

    // Widget expanded view settings
    public string TemperatureUnit { get; set; } = "C"; // "C" or "F"
    public List<AlarmEntry> Alarms { get; set; } = new();

    /// <summary>
    /// Copies all personalization fields from another AppSettings instance.
    /// </summary>
    public void CopyPersonalizationFrom(AppSettings source)
    {
        CompassBoxDefaultText = source.CompassBoxDefaultText;
        PrimaryColor = source.PrimaryColor;
        AccentColor = source.AccentColor;
        SecondaryColor = source.SecondaryColor;
        TextColor = source.TextColor;
        WindowWidth = source.WindowWidth;
        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        AnimationsEnabled = source.AnimationsEnabled;
        BorderColor = source.BorderColor;
        BorderRadius = source.BorderRadius;
        SelectedTheme = source.SelectedTheme;
        CompactMode = source.CompactMode;
        WindowVerticalPosition = source.WindowVerticalPosition;
        ChatBubbleStyle = source.ChatBubbleStyle;

        // Advanced personalization
        BackgroundType = source.BackgroundType;
        GradientStartColor = source.GradientStartColor;
        GradientEndColor = source.GradientEndColor;
        GradientAngle = source.GradientAngle;
        BorderThickness = source.BorderThickness;
        BackgroundImagePath = source.BackgroundImagePath;
        BackgroundImageOpacity = source.BackgroundImageOpacity;
    }
}

public class ProviderConfig
{
    public string ApiKey { get; set; } = "";
    public string SelectedModel { get; set; } = "";
    public string? BaseUrl { get; set; }
}
