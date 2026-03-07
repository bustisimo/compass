using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class FileIndexService : IDisposable
{
    private readonly ILogger<FileIndexService> _logger;
    private readonly ConcurrentDictionary<string, FileIndexEntry> _index = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private const int MaxFiles = 10000;

    public FileIndexService(ILogger<FileIndexService> logger)
    {
        _logger = logger;
    }

    public void StartIndexing(List<string> directories)
    {
        _index.Clear();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();

        Task.Run(() =>
        {
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir)) continue;

                try
                {
                    ScanDirectory(dir);
                    SetupWatcher(dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index directory {Dir}", dir);
                }
            }

            _logger.LogInformation("File index built: {Count} files", _index.Count);
        });
    }

    public List<AppSearchResult> Search(string query)
    {
        return _index.Values
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .Take(5)
            .Select(e => new AppSearchResult
            {
                AppName = e.Name,
                FilePath = e.FullPath,
                GeometryIcon = Geometry.Parse("M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z")
            })
            .ToList();
    }

    private void ScanDirectory(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (_index.Count >= MaxFiles) return;

                var name = Path.GetFileName(file);
                _index.TryAdd(file, new FileIndexEntry { Name = name, FullPath = file });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning {Dir}", dir);
        }
    }

    private void SetupWatcher(string dir)
    {
        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            watcher.Created += (s, e) =>
            {
                if (_index.Count < MaxFiles)
                    _index.TryAdd(e.FullPath, new FileIndexEntry { Name = Path.GetFileName(e.FullPath), FullPath = e.FullPath });
            };

            watcher.Deleted += (s, e) => _index.TryRemove(e.FullPath, out _);

            watcher.Renamed += (s, e) =>
            {
                _index.TryRemove(e.OldFullPath, out _);
                if (_index.Count < MaxFiles)
                    _index.TryAdd(e.FullPath, new FileIndexEntry { Name = Path.GetFileName(e.FullPath), FullPath = e.FullPath });
            };

            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set up FileSystemWatcher for {Dir}", dir);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    private class FileIndexEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
    }
}
