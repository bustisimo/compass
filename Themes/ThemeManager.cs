using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Compass.Themes;

public class ThemeDefinition
{
    public string Name { get; set; } = "";
    public string PrimaryColor { get; set; } = "#1C1C1C";
    public string AccentColor { get; set; } = "#4CC2FF";
    public string BorderColor { get; set; } = "#2D2D2D";
    public double BorderRadius { get; set; } = 12;
    public bool AnimationsEnabled { get; set; } = true;
}

public class ThemeManager
{
    private readonly ILogger<ThemeManager> _logger;

    private static readonly Dictionary<string, ThemeDefinition> BuiltInThemes = new()
    {
        ["Dark"] = new ThemeDefinition
        {
            Name = "Dark",
            PrimaryColor = "#1C1C1C",
            AccentColor = "#4CC2FF",
            BorderColor = "#2D2D2D",
            BorderRadius = 12,
            AnimationsEnabled = true
        },
        ["Light"] = new ThemeDefinition
        {
            Name = "Light",
            PrimaryColor = "#F5F5F5",
            AccentColor = "#0078D4",
            BorderColor = "#D0D0D0",
            BorderRadius = 12,
            AnimationsEnabled = true
        },
        ["High Contrast"] = new ThemeDefinition
        {
            Name = "High Contrast",
            PrimaryColor = "#000000",
            AccentColor = "#FFFF00",
            BorderColor = "#FFFFFF",
            BorderRadius = 0,
            AnimationsEnabled = false
        }
    };

    public ThemeManager(ILogger<ThemeManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<string, ThemeDefinition> GetBuiltInThemes() => BuiltInThemes;

    public ThemeDefinition? GetTheme(string name)
    {
        BuiltInThemes.TryGetValue(name, out var theme);
        return theme;
    }

    public void ApplyTheme(ThemeDefinition theme, AppSettings settings)
    {
        settings.PrimaryColor = theme.PrimaryColor;
        settings.AccentColor = theme.AccentColor;
        settings.BorderColor = theme.BorderColor;
        settings.BorderRadius = theme.BorderRadius;
        settings.AnimationsEnabled = theme.AnimationsEnabled;
        settings.SelectedTheme = theme.Name;
        _logger.LogInformation("Applied theme: {ThemeName}", theme.Name);
    }

    public static void ApplyThemeBrushes(ResourceDictionary resources, bool isLight)
    {
        if (isLight)
        {
            resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            resources["TextPlaceholderBrush"] = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
            resources["CardBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            resources["HoverBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
            resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
            resources["IconBrush"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }
        else
        {
            resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            resources["TextPlaceholderBrush"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15));
            resources["CardBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            resources["HoverBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            resources["IconBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
    }
}
