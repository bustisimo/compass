using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class ClipboardHistoryService
{
    private static readonly string ClipboardFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "clipboard.json");

    private readonly ILogger<ClipboardHistoryService> _logger;
    private List<ClipboardEntry> _entries = new();
    private const int MaxEntries = 50;
    private const int AutoPurgeHours = 24;

    public bool IsEnabled { get; set; } = true;

    public ClipboardHistoryService(ILogger<ClipboardHistoryService> logger)
    {
        _logger = logger;
        Load();
    }

    public void AddEntry(string text)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text)) return;

        // Remove duplicate
        _entries.RemoveAll(e => e.Text == text);

        _entries.Insert(0, new ClipboardEntry { Text = text });

        // Trim to max
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);

        // Auto-purge old unpinned entries
        _entries.RemoveAll(e => !e.IsPinned &&
            (DateTime.UtcNow - e.Timestamp).TotalHours > AutoPurgeHours);

        Save();
    }

    public List<ClipboardEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _entries.Take(10).ToList();

        return _entries
            .Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
    }

    public List<ClipboardEntry> GetAll() => _entries.ToList();

    public void TogglePin(ClipboardEntry entry)
    {
        entry.IsPinned = !entry.IsPinned;
        Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(ClipboardFile))
            {
                string json = File.ReadAllText(ClipboardFile);
                _entries = JsonSerializer.Deserialize<List<ClipboardEntry>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load clipboard history");
            _entries = new();
        }
    }

    private void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(ClipboardFile)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ClipboardFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save clipboard history");
        }
    }
}
