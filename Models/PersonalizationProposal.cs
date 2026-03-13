namespace Compass;

public class PersonalizationProposal
{
    public string CompassBoxDefaultText { get; set; } = "";
    public string PrimaryColor { get; set; } = "";
    public string AccentColor { get; set; } = "";
    public string SecondaryColor { get; set; } = "";
    public string TextColor { get; set; } = "";
    public double? WindowWidth { get; set; }
    public string FontFamily { get; set; } = "";
    public double? FontSize { get; set; }
    public bool? AnimationsEnabled { get; set; }
    public string BorderColor { get; set; } = "";
    public double? BorderRadius { get; set; }
    public bool? CompactMode { get; set; }

    // Advanced personalization - Background
    public string BackgroundType { get; set; } = "";
    public string GradientStartColor { get; set; } = "";
    public string GradientEndColor { get; set; } = "";
    public double? GradientAngle { get; set; }
    public double? BorderThickness { get; set; }

    // Background image generation
    public string BackgroundImagePrompt { get; set; } = "";
    public double? BackgroundImageOpacity { get; set; }

    /// <summary>
    /// Applies non-null/non-empty proposal fields to the target AppSettings.
    /// </summary>
    public void ApplyTo(AppSettings target)
    {
        if (!string.IsNullOrEmpty(CompassBoxDefaultText))
            target.CompassBoxDefaultText = CompassBoxDefaultText;
        if (!string.IsNullOrEmpty(PrimaryColor))
            target.PrimaryColor = PrimaryColor;
        if (!string.IsNullOrEmpty(AccentColor))
            target.AccentColor = AccentColor;
        if (!string.IsNullOrEmpty(SecondaryColor))
            target.SecondaryColor = SecondaryColor;
        if (!string.IsNullOrEmpty(TextColor))
            target.TextColor = TextColor;
        if (WindowWidth.HasValue && WindowWidth > 0)
            target.WindowWidth = WindowWidth.Value;
        if (!string.IsNullOrEmpty(FontFamily))
            target.FontFamily = FontFamily;
        if (FontSize.HasValue && FontSize > 0)
            target.FontSize = FontSize.Value;
        if (AnimationsEnabled.HasValue)
            target.AnimationsEnabled = AnimationsEnabled.Value;
        if (!string.IsNullOrEmpty(BorderColor))
            target.BorderColor = BorderColor;
        if (BorderRadius.HasValue && BorderRadius >= 0)
            target.BorderRadius = BorderRadius.Value;
        if (CompactMode.HasValue)
            target.CompactMode = CompactMode.Value;

        // Advanced personalization
        if (!string.IsNullOrEmpty(BackgroundType))
            target.BackgroundType = BackgroundType;
        if (!string.IsNullOrEmpty(GradientStartColor))
            target.GradientStartColor = GradientStartColor;
        if (!string.IsNullOrEmpty(GradientEndColor))
            target.GradientEndColor = GradientEndColor;
        if (GradientAngle.HasValue)
            target.GradientAngle = GradientAngle.Value;
        if (BorderThickness.HasValue && BorderThickness >= 0)
            target.BorderThickness = BorderThickness.Value;
        if (BackgroundImageOpacity.HasValue && BackgroundImageOpacity >= 0 && BackgroundImageOpacity <= 1)
            target.BackgroundImageOpacity = BackgroundImageOpacity.Value;
        // BackgroundImagePrompt is consumed by the personalization flow, not applied directly
    }
}
