using System.Reflection;
using Compass.Services;

namespace Compass.Tests;

public class GeminiServiceModelFilterTests
{
    // Access the private static method via reflection for testing
    private static bool IsUnavailableModel(string name)
    {
        var method = typeof(GeminiService).GetMethod("IsUnavailableModel",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, new object[] { name })!;
    }

    [Theory]
    [InlineData("gemini-1.0-pro")]
    [InlineData("gemini-1.0-ultra")]
    [InlineData("gemini-pro")]
    [InlineData("gemini-pro-vision")]
    [InlineData("chat-bison-001")]
    [InlineData("text-bison-001")]
    [InlineData("embedding-001")]
    [InlineData("text-embedding-004")]
    [InlineData("aqa")]
    public void IsUnavailableModel_FiltersLegacyModels(string name)
    {
        Assert.True(IsUnavailableModel(name));
    }

    [Theory]
    [InlineData("gemini-1.5-flash-001")]
    [InlineData("gemini-1.5-pro-002")]
    [InlineData("gemini-1.5-flash-8b-001")]
    public void IsUnavailableModel_FiltersDatedGemini15Snapshots(string name)
    {
        Assert.True(IsUnavailableModel(name));
    }

    [Theory]
    [InlineData("gemini-1.5-flash-latest")]
    [InlineData("gemini-1.5-pro-latest")]
    public void IsUnavailableModel_AllowsGemini15LatestAliases(string name)
    {
        Assert.False(IsUnavailableModel(name));
    }

    [Theory]
    [InlineData("gemini-2.0-flash")]
    [InlineData("gemini-2.0-flash-001")]
    [InlineData("gemini-2.0-flash-latest")]
    [InlineData("gemini-2.0-flash-lite")]
    [InlineData("gemini-2.0-flash-exp")]
    public void IsUnavailableModel_FiltersGemini20Family(string name)
    {
        Assert.True(IsUnavailableModel(name));
    }

    [Theory]
    [InlineData("gemini-2.5-flash")]
    [InlineData("gemini-2.5-pro")]
    [InlineData("gemini-2.5-flash-lite")]
    [InlineData("gemini-2.5-flash-exp")]
    public void IsUnavailableModel_AllowsCurrentModels(string name)
    {
        Assert.False(IsUnavailableModel(name));
    }
}
