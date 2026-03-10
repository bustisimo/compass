using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class BookmarkService
{
    private readonly ILogger<BookmarkService> _logger;
    private List<BookmarkEntry> _bookmarks = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _reloadDebounce;

    public BookmarkService(ILogger<BookmarkService> logger)
    {
        _logger = logger;
    }

    public void LoadBookmarks()
    {
        var entries = new List<BookmarkEntry>();

        // Chrome
        var chromePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data", "Default", "Bookmarks");
        if (File.Exists(chromePath))
        {
            entries.AddRange(ParseBookmarkFile(chromePath, "Chrome"));
            WatchFile(chromePath);
        }

        // Edge
        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data", "Default", "Bookmarks");
        if (File.Exists(edgePath))
        {
            entries.AddRange(ParseBookmarkFile(edgePath, "Edge"));
            WatchFile(edgePath);
        }

        _bookmarks = entries;
        _logger.LogInformation("Loaded {Count} bookmarks", entries.Count);
    }

    private List<BookmarkEntry> ParseBookmarkFile(string path, string browser)
    {
        var results = new List<BookmarkEntry>();
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("roots", out var roots))
            {
                foreach (var root in roots.EnumerateObject())
                {
                    if (root.Value.ValueKind == JsonValueKind.Object)
                        ParseBookmarkNode(root.Value, browser, results);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse bookmark file {Path}", path);
        }
        return results;
    }

    private void ParseBookmarkNode(JsonElement node, string browser, List<BookmarkEntry> results)
    {
        if (node.TryGetProperty("type", out var typeProp))
        {
            string type = typeProp.GetString() ?? "";
            if (type == "url")
            {
                string title = node.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string url = node.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
                {
                    results.Add(new BookmarkEntry { Title = title, Url = url, Browser = browser });
                }
            }
            else if (type == "folder")
            {
                if (node.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        ParseBookmarkNode(child, browser, results);
                    }
                }
            }
        }
    }

    private void WatchFile(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            if (dir == null) return;

            var watcher = new FileSystemWatcher(dir, file)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite
            };
            watcher.Changed += (s, e) =>
            {
                // Debounce: wait 500ms after last change before reloading
                _reloadDebounce?.Dispose();
                _reloadDebounce = new Timer(_ =>
                {
                    try { LoadBookmarks(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to reload bookmarks"); }
                }, null, 500, Timeout.Infinite);
            };
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to watch bookmark file {Path}", path);
        }
    }

    public List<BookmarkEntry> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return _bookmarks.Take(maxResults).ToList();

        return _bookmarks
            .Select(b => new { Bookmark = b, Score = FuzzyMatcher.Score(b.Title, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Bookmark)
            .ToList();
    }
}
