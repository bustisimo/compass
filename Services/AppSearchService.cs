using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Compass.Services;

public class AppSearchService
{
    private List<AppSearchResult> _appCache = new();
    private List<AppSearchResult> _shortcutCache = new();

    /// <summary>
    /// Scans disk for .lnk files on a background thread, then assembles the full
    /// cache (virtual commands + extensions + disk apps) on the calling thread.
    /// </summary>
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
            .Select(item => new { Item = item, Score = GetScore(item.AppName, query) })
            .Where(x => x.Score > 0)
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
                Debug.WriteLine($"[Compass] ScanDir '{dir}': {ex.Message}");
            }
        }

        ScanDir(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
        ScanDir(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        return results;
    }

    private static int GetScore(string text, string query)
    {
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (text.IndexOf(" " + query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
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
