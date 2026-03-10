using System.Windows.Media;
using Compass.Services;

namespace Compass.Plugins;

public class SnippetPlugin : ICompassPlugin
{
    private readonly SnippetService _snippetService;

    public SnippetPlugin(SnippetService snippetService)
    {
        _snippetService = snippetService;
    }

    public string Name => "Snippets";
    public string? SearchPrefix => null; // Snippets can match without prefix

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var snippets = _snippetService.Search(query);
        var results = snippets.Select(s => new AppSearchResult
        {
            AppName = $"[Snippet] {s.Title}",
            FilePath = $"SNIPPET:{s.Keyword}",
            GeometryIcon = Geometry.Parse("M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M9,13V15H15V13H9M9,17V19H13V17H9Z")
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.FilePath.StartsWith("SNIPPET:"))
        {
            string keyword = result.FilePath["SNIPPET:".Length..];
            var snippets = _snippetService.GetAll();
            var snippet = snippets.FirstOrDefault(s => s.Keyword == keyword);
            if (snippet != null)
            {
                System.Windows.Clipboard.SetText(snippet.Content);
                _snippetService.RecordUsage(keyword);
                return Task.FromResult<string?>("Snippet copied to clipboard");
            }
        }
        return Task.FromResult<string?>(null);
    }

    public void Dispose() { }
}
