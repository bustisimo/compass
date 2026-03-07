using Microsoft.Extensions.Logging;

namespace Compass.Plugins;

public class PluginHost : IDisposable
{
    private readonly ILogger<PluginHost> _logger;
    private readonly List<ICompassPlugin> _plugins = new();

    public PluginHost(ILogger<PluginHost> logger)
    {
        _logger = logger;
    }

    public void Register(ICompassPlugin plugin)
    {
        _plugins.Add(plugin);
        _logger.LogInformation("Plugin registered: {PluginName}", plugin.Name);
    }

    public async Task InitializeAllAsync()
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize plugin: {PluginName}", plugin.Name);
            }
        }
    }

    public async Task<List<AppSearchResult>> SearchAllAsync(string query)
    {
        var results = new List<AppSearchResult>();

        foreach (var plugin in _plugins)
        {
            try
            {
                // Check if query matches plugin's search prefix
                if (plugin.SearchPrefix != null)
                {
                    if (!query.StartsWith(plugin.SearchPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    query = query[plugin.SearchPrefix.Length..].TrimStart();
                }

                var pluginResults = await plugin.SearchAsync(query);
                results.AddRange(pluginResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin search failed: {PluginName}", plugin.Name);
            }
        }

        return results;
    }

    public async Task<string?> ExecuteAsync(AppSearchResult result)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                var output = await plugin.ExecuteAsync(result);
                if (output != null) return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin execution failed: {PluginName}", plugin.Name);
            }
        }
        return null;
    }

    public IReadOnlyList<ICompassPlugin> Plugins => _plugins.AsReadOnly();

    public void Dispose()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.Dispose(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Plugin dispose failed"); }
        }
        _plugins.Clear();
    }
}
