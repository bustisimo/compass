using System.Diagnostics;
using System.Windows.Media;
using Compass.Services;
using Microsoft.Extensions.Logging;

namespace Compass.Plugins;

public class BookmarkPlugin : ICompassPlugin
{
    private readonly BookmarkService _bookmarkService;
    private readonly ILogger<BookmarkPlugin> _logger;

    public BookmarkPlugin(BookmarkService bookmarkService, ILogger<BookmarkPlugin> logger)
    {
        _bookmarkService = bookmarkService;
        _logger = logger;
    }

    public string Name => "Browser Bookmarks";
    public string? SearchPrefix => "bm:";

    public Task InitializeAsync()
    {
        _bookmarkService.LoadBookmarks();
        return Task.CompletedTask;
    }

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var bookmarks = _bookmarkService.Search(query);
        var results = bookmarks.Select(b => new AppSearchResult
        {
            AppName = b.Title,
            FilePath = $"BOOKMARK:{b.Url}",
            Subtitle = b.Url,
            GeometryIcon = Geometry.Parse("M17,3H7A2,2 0 0,0 5,5V21L12,18L19,21V5A2,2 0 0,0 17,3Z"),
            ResultType = ResultType.Bookmark
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.FilePath.StartsWith("BOOKMARK:"))
        {
            string url = result.FilePath["BOOKMARK:".Length..];
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return Task.FromResult<string?>("Opened bookmark");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to open bookmark: {Url}", url); }
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
