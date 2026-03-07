using Compass.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Tests;

public class CalculatorServiceTests
{
    private readonly CalculatorService _calculator;

    public CalculatorServiceTests()
    {
        _calculator = new CalculatorService(NullLogger<CalculatorService>.Instance);
    }

    [Theory]
    [InlineData("2+3", "= 5")]
    [InlineData("10*5", "= 50")]
    [InlineData("100/4", "= 25")]
    [InlineData("7-3", "= 4")]
    public void TryEvaluate_MathExpressions_ReturnsCorrectResult(string input, string expected)
    {
        var result = _calculator.TryEvaluate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryEvaluate_LengthConversion_Km_To_Mi()
    {
        var result = _calculator.TryEvaluate("100 km to mi");
        Assert.NotNull(result);
        Assert.Contains("km", result);
        Assert.Contains("mi", result);
    }

    [Fact]
    public void TryEvaluate_WeightConversion_Kg_To_Lbs()
    {
        var result = _calculator.TryEvaluate("100 kg to lbs");
        Assert.NotNull(result);
        Assert.Contains("kg", result);
        Assert.Contains("lbs", result);
    }

    [Fact]
    public void TryEvaluate_TemperatureConversion_C_To_F()
    {
        var result = _calculator.TryEvaluate("100 c to f");
        Assert.NotNull(result);
        // 100 C = 212 F
        Assert.Contains("212", result);
    }

    [Fact]
    public void TryEvaluate_StorageConversion_Gb_To_Mb()
    {
        var result = _calculator.TryEvaluate("1 gb to mb");
        Assert.NotNull(result);
        Assert.Contains("1024", result);
    }

    [Fact]
    public void TryEvaluate_InvalidInput_ReturnsNull()
    {
        var result = _calculator.TryEvaluate("hello world");
        Assert.Null(result);
    }

    [Fact]
    public void TryEvaluate_UnknownUnits_ReturnsNull()
    {
        var result = _calculator.TryEvaluate("100 foo to bar");
        Assert.Null(result);
    }

    [Fact]
    public void TryEvaluate_TimeConversion_Hr_To_Min()
    {
        var result = _calculator.TryEvaluate("2 hr to min");
        Assert.NotNull(result);
        Assert.Contains("120", result);
    }
}
