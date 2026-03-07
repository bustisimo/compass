using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Compass.Services;

namespace Compass.Plugins;

public class RecentFilesPlugin : ICompassPlugin
{
    private readonly RecentFilesService _recentFilesService;

    public RecentFilesPlugin(RecentFilesService recentFilesService)
    {
        _recentFilesService = recentFilesService;
    }

    public string Name => "Recent Files";
    public string? SearchPrefix => "r:";

    public Task InitializeAsync()
    {
        _recentFilesService.LoadRecentFiles();
        return Task.CompletedTask;
    }

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var files = _recentFilesService.Search(query);
        var results = files.Select(f => new AppSearchResult
        {
            AppName = f.FileName,
            FilePath = f.TargetPath,
            Subtitle = Path.GetDirectoryName(f.TargetPath) ?? "",
            GeometryIcon = Geometry.Parse("M13,9V3.5L18.5,9M6,2C4.89,2 4,2.89 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2H6Z"),
            ResultType = ResultType.RecentFile
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.ResultType == ResultType.RecentFile)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = result.FilePath, UseShellExecute = true });
                return Task.FromResult<string?>("Opened file");
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to open recent file"); }
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
