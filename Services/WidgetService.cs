using System.IO;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class WidgetService : IWidgetService
{
    public string WidgetsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "Widgets");

    private readonly ILogger<WidgetService> _logger;

    public WidgetService(ILogger<WidgetService> logger)
    {
        _logger = logger;
    }

    public void EnsureWidgetsFolderExists()
    {
        if (!Directory.Exists(WidgetsPath))
            Directory.CreateDirectory(WidgetsPath);
    }

    public List<CompassWidget> LoadCustomWidgets()
    {
        var result = new List<CompassWidget>();
        if (!Directory.Exists(WidgetsPath)) return result;

        foreach (var file in Directory.GetFiles(WidgetsPath, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var widget = JsonSerializer.Deserialize<CompassWidget>(json);
                if (widget != null) result.Add(widget);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load widget from {File}", file);
            }
        }
        return result;
    }

    public void SaveWidget(CompassWidget widget)
    {
        EnsureWidgetsFolderExists();
        string json = JsonSerializer.Serialize(widget, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(WidgetsPath, $"{widget.Id}.json"), json);
    }

    public void DeleteWidget(string widgetId)
    {
        string path = Path.Combine(WidgetsPath, $"{widgetId}.json");
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to delete widget {WidgetId}", widgetId); }
        }
    }

    public List<CompassWidget> GetBuiltInWidgets()
    {
        return new List<CompassWidget>
        {
            new CompassWidget
            {
                Id = "builtin-clock",
                Name = "Clock",
                Description = "Current time and date",
                IsBuiltIn = true,
                BuiltInType = "Clock",
                RefreshIntervalSeconds = 1,
                WidgetSize = "1x1"
            },
            new CompassWidget
            {
                Id = "builtin-weather",
                Name = "Weather",
                Description = "Current weather conditions",
                IsBuiltIn = true,
                BuiltInType = "Weather",
                RefreshIntervalSeconds = 1800,
                WidgetSize = "1x1"
            },
            new CompassWidget
            {
                Id = "builtin-systemstats",
                Name = "System Stats",
                Description = "CPU, RAM, and disk usage",
                IsBuiltIn = true,
                BuiltInType = "SystemStats",
                RefreshIntervalSeconds = 5,
                WidgetSize = "1x1"
            },
            new CompassWidget
            {
                Id = "builtin-calendar",
                Name = "Calendar",
                Description = "Upcoming Outlook calendar events",
                IsBuiltIn = true,
                BuiltInType = "Calendar",
                RefreshIntervalSeconds = 300,
                WidgetSize = "2x1"
            },
            new CompassWidget
            {
                Id = "builtin-media",
                Name = "Now Playing",
                Description = "Media playback controls",
                IsBuiltIn = true,
                BuiltInType = "Media",
                RefreshIntervalSeconds = 3,
                WidgetSize = "1x1"
            },
            new CompassWidget
            {
                Id = "builtin-notes",
                Name = "Notes",
                Description = "Quick notes and scratchpad",
                IsBuiltIn = true,
                BuiltInType = "Notes",
                RefreshIntervalSeconds = 0,
                WidgetSize = "2x1"
            }
        };
    }

    public List<CompassWidget> GetAllWidgets()
    {
        var all = GetBuiltInWidgets();
        all.AddRange(LoadCustomWidgets());
        return all;
    }
}
