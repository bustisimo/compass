namespace Compass.Services.Interfaces;

public interface IGeminiService
{
    bool HasHistory { get; }
    void ClearHistory();
    List<(string role, string text)> GetExportableHistory();
    List<(string role, string text, List<(byte[] data, string mimeType)> images)> GetExportableHistoryWithImages();
    string SerializeHistory();
    void LoadSerializedHistory(string json);
    Task<GeminiResponse> AskAsync(string prompt, AppSettings settings, CancellationToken cancellationToken = default, string? modelOverride = null, List<(byte[] data, string mimeType)>? images = null);
    Task<GeminiResponse> AskStreamingAsync(string prompt, AppSettings settings, Action<string> onChunk, CancellationToken cancellationToken = default, string? modelOverride = null);
    Task<string> GeneratePowerShellScriptAsync(string intent, AppSettings settings, CancellationToken cancellationToken = default);
    Task<List<string>> FetchAvailableModelsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<string> GenerateWidgetXamlAsync(string description, AppSettings settings, CancellationToken cancellationToken = default);
    Task<(byte[] data, string mimeType)?> GenerateImageAsync(string prompt, AppSettings settings, CancellationToken cancellationToken = default);
    void RemoveLastExchange();
}
