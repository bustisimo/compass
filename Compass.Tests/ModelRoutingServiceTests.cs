using Compass.Services;

namespace Compass.Tests;

public class ModelRoutingServiceTests
{
    private readonly ModelRoutingService _service = new();

    [Theory]
    [InlineData("what time is it", QueryComplexity.Simple)]
    [InlineData("cpu usage", QueryComplexity.Simple)]
    [InlineData("battery", QueryComplexity.Simple)]
    public void ClassifyQuery_SimplePatterns_ReturnsSimple(string query, QueryComplexity expected)
    {
        Assert.Equal(expected, _service.ClassifyQuery(query));
    }

    [Theory]
    [InlineData("explain quantum computing in detail", QueryComplexity.Complex)]
    [InlineData("write a function to sort arrays", QueryComplexity.Complex)]
    [InlineData("analyze this code", QueryComplexity.Complex)]
    public void ClassifyQuery_ComplexPatterns_ReturnsComplex(string query, QueryComplexity expected)
    {
        Assert.Equal(expected, _service.ClassifyQuery(query));
    }

    [Fact]
    public void ClassifyQuery_ShortGenericQuery_ReturnsSimple()
    {
        Assert.Equal(QueryComplexity.Simple, _service.ClassifyQuery("hello"));
    }

    [Fact]
    public void ClassifyQuery_EmptyQuery_ReturnsStandard()
    {
        Assert.Equal(QueryComplexity.Standard, _service.ClassifyQuery(""));
    }

    [Fact]
    public void SelectModel_RoutingDisabled_ReturnsSelectedModel()
    {
        var settings = new AppSettings
        {
            SmartRoutingEnabled = false,
            SelectedModel = "my-model"
        };

        Assert.Equal("my-model", _service.SelectModel("explain quantum physics", settings));
    }

    [Fact]
    public void SelectModel_RoutingEnabled_SimpleQuery_ReturnsFastModel()
    {
        var settings = new AppSettings
        {
            SmartRoutingEnabled = true,
            FastModel = "fast-model",
            PowerModel = "power-model",
            SelectedModel = "default-model"
        };

        Assert.Equal("fast-model", _service.SelectModel("what time is it", settings));
    }

    [Fact]
    public void SelectModel_RoutingEnabled_ComplexQuery_ReturnsPowerModel()
    {
        var settings = new AppSettings
        {
            SmartRoutingEnabled = true,
            FastModel = "fast-model",
            PowerModel = "power-model",
            SelectedModel = "default-model"
        };

        Assert.Equal("power-model", _service.SelectModel("explain the theory of relativity in detail", settings));
    }

    [Fact]
    public void SelectModel_WithImages_ReturnsPowerModel()
    {
        var settings = new AppSettings
        {
            SmartRoutingEnabled = true,
            FastModel = "fast-model",
            PowerModel = "power-model"
        };

        Assert.Equal("power-model", _service.SelectModel("hello", settings, hasImages: true));
    }
}
