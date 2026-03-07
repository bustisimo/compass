using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class FrecencyTracker
{
    private static readonly string FrecencyFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "frecency.json");

    private readonly ILogger<FrecencyTracker> _logger;
    private Dictionary<string, FrecencyEntry> _entries = new();
    private const double DecayFactor = 0.95; // decay per day

    public FrecencyTracker(ILogger<FrecencyTracker> logger)
    {
        _logger = logger;
        Load();
    }

    public void RecordLaunch(string appName)
    {
        if (!_entries.TryGetValue(appName, out var entry))
        {
            entry = new FrecencyEntry();
            _entries[appName] = entry;
        }

        entry.LaunchCount++;
        entry.LastLaunched = DateTime.UtcNow;
        Save();
    }

    /// <summary>
    /// Returns a frecency score (0.0–1.0) for the given app name.
    /// </summary>
    public double GetScore(string appName)
    {
        if (!_entries.TryGetValue(appName, out var entry))
            return 0;

        double daysSinceLastUse = (DateTime.UtcNow - entry.LastLaunched).TotalDays;
        double recency = Math.Pow(DecayFactor, daysSinceLastUse);
        double frequency = Math.Min(entry.LaunchCount / 20.0, 1.0); // cap at 20 uses

        return recency * 0.6 + frequency * 0.4;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FrecencyFile))
            {
                string json = File.ReadAllText(FrecencyFile);
                _entries = JsonSerializer.Deserialize<Dictionary<string, FrecencyEntry>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load frecency data");
            _entries = new();
        }
    }

    private void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(FrecencyFile)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FrecencyFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save frecency data");
        }
    }

    private class FrecencyEntry
    {
        public int LaunchCount { get; set; }
        public DateTime LastLaunched { get; set; } = DateTime.UtcNow;
    }
}
