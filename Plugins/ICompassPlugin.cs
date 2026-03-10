namespace Compass.Plugins;

public interface ICompassPlugin : IDisposable
{
    string Name { get; }
    string? SearchPrefix { get; }
    Task InitializeAsync();
    Task<List<AppSearchResult>> SearchAsync(string query);
    Task<string?> ExecuteAsync(AppSearchResult result);
}
