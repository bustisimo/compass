namespace Compass;

public class PersonalizationProposal
{
    public string CompassBoxDefaultText { get; set; } = "";
    public string PrimaryColor { get; set; } = "";
    public string AccentColor { get; set; } = "";
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public string FontFamily { get; set; } = "";
    public double? FontSize { get; set; }
    public bool? AnimationsEnabled { get; set; }
    public string BorderColor { get; set; } = "";
    public double? BorderRadius { get; set; }
}
