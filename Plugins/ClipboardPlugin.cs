using System.Windows.Media;
using Compass.Services;

namespace Compass.Plugins;

public class ClipboardPlugin : ICompassPlugin
{
    private readonly ClipboardHistoryService _clipboardService;

    public ClipboardPlugin(ClipboardHistoryService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public string Name => "Clipboard History";
    public string? SearchPrefix => "cb:";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var entries = _clipboardService.Search(query);
        var results = entries.Select(e => new AppSearchResult
        {
            AppName = e.Text.Length > 60 ? e.Text[..60] + "..." : e.Text,
            FilePath = $"CLIPBOARD:{e.Timestamp.Ticks}",
            GeometryIcon = Geometry.Parse("M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M7,7H17V5H19V19H5V5H7V7Z")
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.FilePath.StartsWith("CLIPBOARD:"))
        {
            // Find the matching entry and paste it
            string ticksStr = result.FilePath["CLIPBOARD:".Length..];
            if (long.TryParse(ticksStr, out long ticks))
            {
                var entries = _clipboardService.GetAll();
                var entry = entries.FirstOrDefault(e => e.Timestamp.Ticks == ticks);
                if (entry != null)
                {
                    System.Windows.Clipboard.SetText(entry.Text);
                    return Task.FromResult<string?>("Pasted from clipboard history");
                }
            }
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
