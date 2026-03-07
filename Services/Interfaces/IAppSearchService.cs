namespace Compass.Services.Interfaces;

public interface IAppSearchService
{
    Task RefreshCacheAsync(List<CompassExtension> extensions);
    void RefreshShortcutCache(List<CustomShortcut> shortcuts);
    List<AppSearchResult> Search(string query, bool hasHistory);
    List<AppSearchResult> SearchCommands(string query);
    void RecordLaunch(string appName);
}
