using System.Diagnostics;
using System.Windows.Media;
using Compass.Services;
using Microsoft.Extensions.Logging;

namespace Compass.Plugins;

public class FileSearchPlugin : ICompassPlugin
{
    private readonly FileContentSearchService _searchService;
    private readonly ILogger<FileSearchPlugin> _logger;

    public FileSearchPlugin(FileContentSearchService searchService, ILogger<FileSearchPlugin> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    public string Name => "File Content Search";
    public string? SearchPrefix => "f:";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var matches = _searchService.Search(query);
        var results = matches.Select(m => new AppSearchResult
        {
            AppName = m.FileName,
            FilePath = m.FilePath,
            Subtitle = $"Line {m.LineNumber}: {(m.MatchingLine.Length > 80 ? m.MatchingLine[..80] + "..." : m.MatchingLine)}",
            GeometryIcon = Geometry.Parse("M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M9,13V15H15V13H9M9,17V19H13V17H9Z"),
            ResultType = ResultType.FileContent
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.ResultType == ResultType.FileContent)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = result.FilePath, UseShellExecute = true });
                return Task.FromResult<string?>("Opened file");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to open file: {Path}", result.FilePath); }
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
