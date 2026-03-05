namespace Compass;

public class AppSettings
{
    public string ApiKey { get; set; } = "";
    public double WindowOpacity { get; set; } = 1.0;
    public bool LaunchAtStartup { get; set; } = false;
    public string SystemPrompt { get; set; } = "You are a helpful desktop assistant.";
    public string SelectedModel { get; set; } = "gemini-1.5-flash";
    public List<string> AvailableModels { get; set; } = new List<string> { "gemini-1.5-flash" };

    // Personalization settings
    public string CompassBoxDefaultText { get; set; } = "Ask Gemini or search apps...";
    public string PrimaryColor { get; set; } = "#1C1C1C";
    public string AccentColor { get; set; } = "#4CC2FF";
    public double WindowWidth { get; set; } = 700;
    public double WindowHeight { get; set; } = 0; // 0 = Auto
    public string FontFamily { get; set; } = "Segoe UI Variable Display, Segoe UI";
    public double FontSize { get; set; } = 14;
    public bool AnimationsEnabled { get; set; } = true;
    public string BorderColor { get; set; } = "#2D2D2D";
    public double BorderRadius { get; set; } = 12;
}
