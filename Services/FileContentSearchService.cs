using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class FileContentSearchService
{
    private readonly ILogger<FileContentSearchService> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _invertedIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _fileLines = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _isIndexing;

    public FileContentSearchService(ILogger<FileContentSearchService> logger)
    {
        _logger = logger;
    }

    public bool IsIndexing => _isIndexing;

    public void StartIndexing(List<string> directories, List<string>? extensions = null, int maxFileSizeKb = 1024)
    {
        if (_isIndexing) return;
        _isIndexing = true;

        var allowedExtensions = extensions ?? new List<string> { ".txt", ".cs", ".json", ".xml", ".md", ".yaml", ".yml", ".ini", ".cfg", ".log", ".csv", ".html", ".css", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".sql" };

        Task.Run(() =>
        {
            try
            {
                int fileCount = 0;
                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir)) continue;

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                        {
                            if (fileCount >= 10000) break;

                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (!allowedExtensions.Contains(ext)) continue;

                            try
                            {
                                var info = new FileInfo(file);
                                if (info.Length > maxFileSizeKb * 1024) continue;

                                IndexFile(file);
                                fileCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to index file {File}", file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enumerate directory {Dir}", dir);
                    }

                    // Set up FileSystemWatcher
                    try
                    {
                        var watcher = new FileSystemWatcher(dir)
                        {
                            IncludeSubdirectories = true,
                            EnableRaisingEvents = true,
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                        };
                        watcher.Changed += (s, e) => { if (allowedExtensions.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant())) IndexFile(e.FullPath); };
                        watcher.Created += (s, e) => { if (allowedExtensions.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant())) IndexFile(e.FullPath); };
                        watcher.Deleted += (s, e) => RemoveFile(e.FullPath);
                        _watchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Dir}", dir);
                    }
                }

                _logger.LogInformation("File content indexing complete: {Count} files indexed", fileCount);
            }
            finally
            {
                _isIndexing = false;
            }
        });
    }

    private void IndexFile(string filePath)
    {
        try
        {
            RemoveFile(filePath);
            var lines = File.ReadAllLines(filePath);
            _fileLines[filePath] = new List<string>(lines);

            foreach (var line in lines)
            {
                var words = line.Split(new[] { ' ', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '<', '>', '/', '\\', '"', '\'', '=', '+', '-', '*', '&', '|', '!', '?', '#', '@' },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (var word in words)
                {
                    var lower = word.ToLowerInvariant();
                    if (lower.Length < 2 || lower.Length > 100) continue;

                    _invertedIndex.AddOrUpdate(lower,
                        _ => new HashSet<string> { filePath },
                        (_, set) => { lock (set) { set.Add(filePath); } return set; });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to index file {File}", filePath);
        }
    }

    private void RemoveFile(string filePath)
    {
        _fileLines.TryRemove(filePath, out _);
        foreach (var kvp in _invertedIndex)
        {
            lock (kvp.Value)
            {
                kvp.Value.Remove(filePath);
            }
        }
    }

    public List<FileContentMatch> Search(string query, int maxResults = 10)
    {
        var results = new List<FileContentMatch>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length == 0) return results;

        // Find files containing all query words (intersect posting lists)
        HashSet<string>? matchingFiles = null;
        foreach (var word in queryWords)
        {
            if (_invertedIndex.TryGetValue(word, out var files))
            {
                HashSet<string> copy;
                lock (files) { copy = new HashSet<string>(files); }

                if (matchingFiles == null)
                    matchingFiles = copy;
                else
                    matchingFiles.IntersectWith(copy);
            }
            else
            {
                return results; // word not found anywhere
            }
        }

        if (matchingFiles == null || matchingFiles.Count == 0) return results;

        // Find matching lines in each file
        foreach (var filePath in matchingFiles.Take(maxResults * 2))
        {
            if (!_fileLines.TryGetValue(filePath, out var lines)) continue;

            for (int i = 0; i < lines.Count; i++)
            {
                var lineLower = lines[i].ToLowerInvariant();
                if (queryWords.All(w => lineLower.Contains(w)))
                {
                    results.Add(new FileContentMatch
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        MatchingLine = lines[i].Trim(),
                        LineNumber = i + 1
                    });
                    break; // one match per file
                }
            }

            if (results.Count >= maxResults) break;
        }

        return results;
    }
}
