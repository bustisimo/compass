using System.IO;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class RecentFilesService
{
    private readonly ILogger<RecentFilesService> _logger;
    private List<(string TargetPath, string FileName, DateTime LastAccessed)> _recentFiles = new();
    private FileSystemWatcher? _watcher;

    public RecentFilesService(ILogger<RecentFilesService> logger)
    {
        _logger = logger;
    }

    public void LoadRecentFiles()
    {
        var results = new List<(string TargetPath, string FileName, DateTime LastAccessed)>();
        var recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        if (!Directory.Exists(recentDir)) return;

        try
        {
            foreach (var lnkFile in Directory.GetFiles(recentDir, "*.lnk"))
            {
                try
                {
                    // Resolve .lnk target using Shell32 COM
                    var targetPath = ResolveLnkTarget(lnkFile);
                    if (string.IsNullOrEmpty(targetPath)) continue;
                    if (!File.Exists(targetPath) && !Directory.Exists(targetPath)) continue;

                    var fileInfo = new FileInfo(lnkFile);
                    results.Add((targetPath, Path.GetFileName(targetPath), fileInfo.LastWriteTime));
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Skipping recent file {File}", lnkFile); }
            }

            _recentFiles = results.OrderByDescending(r => r.LastAccessed).Take(100).ToList();
            _logger.LogInformation("Loaded {Count} recent files", _recentFiles.Count);

            // Watch for changes
            if (_watcher == null)
            {
                _watcher = new FileSystemWatcher(recentDir, "*.lnk")
                {
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                _watcher.Created += (s, e) => LoadRecentFiles();
                _watcher.Changed += (s, e) => LoadRecentFiles();
                _watcher.Deleted += (s, e) => LoadRecentFiles();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load recent files");
        }
    }

    private static string? ResolveLnkTarget(string lnkPath)
    {
        try
        {
            // Use COM Shell32 to resolve .lnk target
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string targetPath = shortcut.TargetPath;

            // Release COM objects
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);

            return string.IsNullOrEmpty(targetPath) ? null : targetPath;
        }
        catch
        {
            return null;
        }
    }

    public List<(string TargetPath, string FileName, DateTime LastAccessed)> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _recentFiles.Take(maxResults).ToList();

        return _recentFiles
            .Select(r => new { File = r, Score = FuzzyMatcher.Score(r.FileName, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.File)
            .ToList();
    }
}
