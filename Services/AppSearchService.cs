using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class AppSearchService : IAppSearchService
{
    private List<AppSearchResult> _appCache = new();
    private List<AppSearchResult> _shortcutCache = new();

    private readonly ILogger<AppSearchService> _logger;
    private readonly FrecencyTracker _frecencyTracker;

    public AppSearchService(ILogger<AppSearchService> logger, FrecencyTracker frecencyTracker)
    {
        _logger = logger;
        _frecencyTracker = frecencyTracker;
    }

    public void RecordLaunch(string appName) => _frecencyTracker.RecordLaunch(appName);

    public async Task RefreshCacheAsync(List<CompassExtension> extensions)
    {
        var diskResults = await Task.Run(() => ScanAppsFromDisk());

        // Build virtual commands on the UI thread (Geometry.Parse requires it)
        var cache = new List<AppSearchResult>
        {
            new AppSearchResult
            {
                AppName = "Compass Settings",
                FilePath = "COMMAND:SETTINGS",
                GeometryIcon = Geometry.Parse("M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z")
            },
            new AppSearchResult
            {
                AppName = "Manage Shortcuts",
                FilePath = "COMMAND:SHORTCUTS",
                GeometryIcon = Geometry.Parse("M4,6H20V8H4V6M4,11H20V13H4V11M4,16H20V18H4V16Z")
            },
            new AppSearchResult
            {
                AppName = "Manage Commands",
                FilePath = "COMMAND:COMMANDS",
                GeometryIcon = Geometry.Parse("M19,6H5A2,2 0 0,0 3,8V16A2,2 0 0,0 5,18H19A2,2 0 0,0 21,16V8A2,2 0 0,0 19,6M10,15V9L15,12L10,15Z")
            },
            // Media controls
            new AppSearchResult
            {
                AppName = "Play / Pause",
                FilePath = "COMMAND:MEDIA_PLAY_PAUSE",
                GeometryIcon = Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z")
            },
            new AppSearchResult
            {
                AppName = "Next Track",
                FilePath = "COMMAND:MEDIA_NEXT",
                GeometryIcon = Geometry.Parse("M16,18H18V6H16M6,18L14.5,12L6,6V18Z")
            },
            new AppSearchResult
            {
                AppName = "Previous Track",
                FilePath = "COMMAND:MEDIA_PREV",
                GeometryIcon = Geometry.Parse("M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z")
            },
            new AppSearchResult
            {
                AppName = "Volume Up",
                FilePath = "COMMAND:VOLUME_UP",
                GeometryIcon = Geometry.Parse("M14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.84 14,18.7V20.77C18,19.86 21,16.28 21,12C21,7.72 18,4.14 14,3.23M16.5,12C16.5,10.23 15.5,8.71 14,7.97V16C15.5,15.29 16.5,13.76 16.5,12M3,9V15H7L12,20V4L7,9H3Z")
            },
            new AppSearchResult
            {
                AppName = "Volume Down",
                FilePath = "COMMAND:VOLUME_DOWN",
                GeometryIcon = Geometry.Parse("M18.5,12C18.5,10.23 17.5,8.71 16,7.97V16C17.5,15.29 18.5,13.76 18.5,12M5,9V15H9L14,20V4L9,9H5Z")
            },
            new AppSearchResult
            {
                AppName = "Mute Volume",
                FilePath = "COMMAND:VOLUME_MUTE",
                GeometryIcon = Geometry.Parse("M12,4L9.91,6.09L12,8.18M4.27,3L3,4.27L7.73,9H3V15H7L12,20V13.27L16.25,17.53C15.58,18.04 14.83,18.45 14,18.7V20.77C15.38,20.45 16.63,19.82 17.68,18.96L19.73,21L21,19.73L12,10.73M19,12C19,12.94 18.8,13.82 18.46,14.64L19.97,16.15C20.62,14.91 21,13.5 21,12C21,7.72 18,4.14 14,3.23V5.29C16.89,6.15 19,8.83 19,12M16.5,12C16.5,10.23 15.5,8.71 14,7.97V10.18L16.45,12.63C16.5,12.43 16.5,12.21 16.5,12Z")
            },
            // Quick settings
            new AppSearchResult
            {
                AppName = "Toggle WiFi",
                FilePath = "COMMAND:TOGGLE_WIFI",
                GeometryIcon = Geometry.Parse("M12,21L15.6,16.2C14.6,15.45 13.35,15 12,15C10.65,15 9.4,15.45 8.4,16.2L12,21M12,3C7.95,3 4.21,4.34 1.2,6.6L3,9C5.5,7.12 8.62,6 12,6C15.38,6 18.5,7.12 21,9L22.8,6.6C19.79,4.34 16.05,3 12,3M12,9C9.3,9 6.81,9.89 4.8,11.4L6.6,13.8C8.1,12.67 9.97,12 12,12C14.03,12 15.9,12.67 17.4,13.8L19.2,11.4C17.19,9.89 14.7,9 12,9Z")
            },
            new AppSearchResult
            {
                AppName = "Toggle Bluetooth",
                FilePath = "COMMAND:TOGGLE_BLUETOOTH",
                GeometryIcon = Geometry.Parse("M14.88,16.29L13,18.17V14.41M13,5.83L14.88,7.71L13,9.58M17.71,7.71L12,2H11V9.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L11,14.41V22H12L17.71,16.29L13.41,12L17.71,7.71Z")
            },
            new AppSearchResult
            {
                AppName = "Do Not Disturb",
                FilePath = "COMMAND:DO_NOT_DISTURB",
                GeometryIcon = Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20M7,13H17V11H7")
            },
            // Window management
            new AppSearchResult
            {
                AppName = "Minimize Window",
                FilePath = "COMMAND:WINDOW_MINIMIZE",
                GeometryIcon = Geometry.Parse("M20,14H4V10H20")
            },
            new AppSearchResult
            {
                AppName = "Maximize Window",
                FilePath = "COMMAND:WINDOW_MAXIMIZE",
                GeometryIcon = Geometry.Parse("M4,4H20V20H4V4M6,8V18H18V8H6Z")
            },
            new AppSearchResult
            {
                AppName = "Snap Window Left",
                FilePath = "COMMAND:SNAP_LEFT",
                GeometryIcon = Geometry.Parse("M4,4H12V20H4V4M14,4H20V20H14V4M6,6V18H10V6H6Z")
            },
            new AppSearchResult
            {
                AppName = "Snap Window Right",
                FilePath = "COMMAND:SNAP_RIGHT",
                GeometryIcon = Geometry.Parse("M4,4H10V20H4V4M12,4H20V20H12V4M14,6V18H18V6H14Z")
            },
            // Layout commands (Feature 12)
            new AppSearchResult
            {
                AppName = "Layout: Split Screen",
                FilePath = "COMMAND:LAYOUT_SPLIT",
                Subtitle = "Snap top 2 windows side by side",
                GeometryIcon = Geometry.Parse("M4,4H12V20H4V4M14,4H20V20H14V4M6,6V18H10V6H6Z"),
                ResultType = ResultType.WindowLayout
            },
            new AppSearchResult
            {
                AppName = "Layout: Stack/Maximize",
                FilePath = "COMMAND:LAYOUT_STACK",
                Subtitle = "Maximize the foreground window",
                GeometryIcon = Geometry.Parse("M4,4H20V20H4V4M6,8V18H18V8H6Z"),
                ResultType = ResultType.WindowLayout
            },
            new AppSearchResult
            {
                AppName = "Layout: Thirds",
                FilePath = "COMMAND:LAYOUT_THIRDS",
                Subtitle = "Arrange top 3 windows in thirds",
                GeometryIcon = Geometry.Parse("M4,4H10V20H4V4M11,4H15V20H11V4M16,4H20V20H16V4Z"),
                ResultType = ResultType.WindowLayout
            }
        };

        foreach (var ext in extensions)
        {
            cache.Add(new AppSearchResult
            {
                AppName = ext.TriggerName,
                FilePath = $"EXTENSION:{ext.TriggerName}",
                GeometryIcon = Geometry.Parse("M19,6H5A2,2 0 0,0 3,8V16A2,2 0 0,0 5,18H19A2,2 0 0,0 21,16V8A2,2 0 0,0 19,6M10,15V9L15,12L10,15Z")
            });
        }

        cache.AddRange(diskResults);
        _appCache = cache;
    }

    public void RefreshShortcutCache(List<CustomShortcut> shortcuts)
    {
        _shortcutCache = shortcuts.Select(s => new AppSearchResult
        {
            AppName = s.Keyword,
            FilePath = "SHORTCUT:" + s.Keyword,
            GeometryIcon = Geometry.Parse("M4,6H20V8H4V6M4,11H20V13H4V11M4,16H20V18H4V16Z")
        }).ToList();
    }

    public List<AppSearchResult> SearchCommands(string query)
    {
        var commands = _appCache.Where(x => x.FilePath.StartsWith("EXTENSION:")).ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            return commands
                .OrderBy(x => x.AppName)
                .Take(10)
                .ToList();
        }

        return commands
            .Select(item => new { Item = item, Score = FuzzyMatcher.Score(item.AppName, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.AppName)
            .Take(10)
            .Select(x => x.Item)
            .ToList();
    }

    public List<AppSearchResult> Search(string query, bool hasHistory)
    {
        var candidates = _appCache.Concat(_shortcutCache).ToList();

        if (hasHistory)
        {
            candidates.Add(new AppSearchResult
            {
                AppName = "Resume Chat",
                FilePath = "COMMAND:RESUME",
                GeometryIcon = Geometry.Parse("M20,2H4A2,2 0 0,0 2,4V22L6,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2M20,16H6L4,18V4H20")
            });
        }

        return candidates
            .DistinctBy(x => x.AppName)
            .Select(item =>
            {
                double fuzzyScore = FuzzyMatcher.Score(item.AppName, query);
                double frecencyScore = _frecencyTracker.GetScore(item.AppName);
                double finalScore = fuzzyScore * 0.6 + frecencyScore * 0.4;
                return new { Item = item, Score = finalScore, FuzzyScore = fuzzyScore };
            })
            .Where(x => x.FuzzyScore > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.AppName)
            .Take(5)
            .Select(x => x.Item)
            .ToList();
    }

    // Runs on a background thread via Task.Run
    private List<AppSearchResult> ScanAppsFromDisk()
    {
        var results = new List<AppSearchResult>();

        void ScanDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    results.Add(new AppSearchResult
                    {
                        AppName = Path.GetFileNameWithoutExtension(f),
                        FilePath = f,
                        AppIcon = GetIcon(f)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan directory {Dir}", dir);
            }
        }

        ScanDir(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
        ScanDir(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        return results;
    }

    // Called from a background thread; freezes the BitmapSource so it can be used on the UI thread.
    private static ImageSource? GetIcon(string filePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
