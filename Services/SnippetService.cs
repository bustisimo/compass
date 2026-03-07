using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class SnippetService
{
    private static readonly string SnippetsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "snippets.json");

    private readonly ILogger<SnippetService> _logger;
    private List<Snippet> _snippets = new();

    public SnippetService(ILogger<SnippetService> logger)
    {
        _logger = logger;
        Load();
    }

    public List<Snippet> GetAll() => _snippets.ToList();

    public void Add(Snippet snippet)
    {
        _snippets.Add(snippet);
        Save();
    }

    public void Update(Snippet snippet)
    {
        var existing = _snippets.FirstOrDefault(s => s.Keyword == snippet.Keyword);
        if (existing != null)
        {
            existing.Title = snippet.Title;
            existing.Content = snippet.Content;
            existing.Category = snippet.Category;
        }
        Save();
    }

    public void Delete(string keyword)
    {
        _snippets.RemoveAll(s => s.Keyword == keyword);
        Save();
    }

    public void RecordUsage(string keyword)
    {
        var snippet = _snippets.FirstOrDefault(s => s.Keyword == keyword);
        if (snippet != null)
        {
            snippet.UsageCount++;
            Save();
        }
    }

    public List<Snippet> Search(string query)
    {
        return _snippets
            .Where(s => s.Keyword.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.UsageCount)
            .Take(5)
            .ToList();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SnippetsFile))
            {
                string json = File.ReadAllText(SnippetsFile);
                _snippets = JsonSerializer.Deserialize<List<Snippet>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load snippets");
            _snippets = new();
        }
    }

    private void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SnippetsFile)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_snippets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SnippetsFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save snippets");
        }
    }
}
