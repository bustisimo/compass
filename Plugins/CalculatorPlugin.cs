using System.Windows.Media;
using Compass.Services;

namespace Compass.Plugins;

public class CalculatorPlugin : ICompassPlugin
{
    private readonly CalculatorService _calculatorService;

    public CalculatorPlugin(CalculatorService calculatorService)
    {
        _calculatorService = calculatorService;
    }

    public string Name => "Calculator";
    public string? SearchPrefix => null; // No prefix — matches math expressions directly

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var results = new List<AppSearchResult>();
        var result = _calculatorService.TryEvaluate(query);
        if (result != null)
        {
            results.Add(new AppSearchResult
            {
                AppName = result,
                FilePath = "MATH",
                GeometryIcon = Geometry.Parse("M19,3H5C3.89,3 3,3.89 3,5V19C3,20.1 3.89,21 5,21H19C20.1,21 21,20.1 21,19V5C21,3.89 20.1,3 19,3M7.5,18C6.67,18 6,17.33 6,16.5C6,15.67 6.67,15 7.5,15C8.33,15 9,15.67 9,16.5C9,17.33 8.33,18 7.5,18M7.5,13C6.67,13 6,12.33 6,11.5C6,10.67 6.67,10 7.5,10C8.33,10 9,10.67 9,11.5C9,12.33 8.33,13 7.5,13M12,18C11.17,18 10.5,17.33 10.5,16.5C10.5,15.67 11.17,15 12,15C12.83,15 13.5,15.67 13.5,16.5C13.5,17.33 12.83,18 12,18M12,13C11.17,13 10.5,12.33 10.5,11.5C10.5,10.67 11.17,10 12,10C12.83,10 13.5,10.67 13.5,11.5C13.5,12.33 12.83,13 12,13M18,17.25H15V15.75H18V17.25M18,12.75H15V11.25H18V12.75M18,8H6V6H18V8Z")
            });
        }
        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.FilePath == "MATH")
        {
            System.Windows.Clipboard.SetText(result.AppName);
            return Task.FromResult<string?>("Copied to clipboard");
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
