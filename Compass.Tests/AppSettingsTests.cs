namespace Compass.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveExpectedValues()
    {
        var settings = new AppSettings();

        Assert.Equal(1.0, settings.WindowOpacity);
        Assert.False(settings.LaunchAtStartup);
        Assert.Equal("gemini-2.5-flash", settings.SelectedModel);
        Assert.Equal("#1C1C1C", settings.PrimaryColor);
        Assert.Equal("#4CC2FF", settings.AccentColor);
        Assert.Equal(700, settings.WindowWidth);
        Assert.Equal(14, settings.FontSize);
        Assert.True(settings.AnimationsEnabled);
        Assert.Equal("Dark", settings.SelectedTheme);
        Assert.Equal("Gemini", settings.ActiveProvider);
        Assert.True(settings.ClipboardHistoryEnabled);
        Assert.False(settings.FileSearchEnabled);
    }

    [Fact]
    public void CopyPersonalizationFrom_CopiesAllFields()
    {
        var source = new AppSettings
        {
            PrimaryColor = "#FF0000",
            AccentColor = "#00FF00",
            FontSize = 18,
            BorderRadius = 20,
            CompactMode = true,
            ChatBubbleStyle = "Square",
            SelectedTheme = "Light"
        };

        var target = new AppSettings();
        target.CopyPersonalizationFrom(source);

        Assert.Equal("#FF0000", target.PrimaryColor);
        Assert.Equal("#00FF00", target.AccentColor);
        Assert.Equal(18, target.FontSize);
        Assert.Equal(20, target.BorderRadius);
        Assert.True(target.CompactMode);
        Assert.Equal("Square", target.ChatBubbleStyle);
        Assert.Equal("Light", target.SelectedTheme);
    }

    [Fact]
    public void CopyPersonalizationFrom_DoesNotCopyNonPersonalizationFields()
    {
        var source = new AppSettings
        {
            SelectedModel = "custom-model",
            SmartRoutingEnabled = true,
            ApiKey = "secret-key"
        };

        var target = new AppSettings();
        target.CopyPersonalizationFrom(source);

        // Non-personalization fields should remain at defaults
        Assert.Equal("gemini-2.5-flash", target.SelectedModel);
        Assert.False(target.SmartRoutingEnabled);
        Assert.Equal("", target.ApiKey);
    }
}
