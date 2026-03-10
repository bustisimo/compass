namespace Compass.Services.Interfaces;

public interface IGeminiService
{
    bool HasHistory { get; }
    void ClearHistory();
    List<(string role, string text)> GetExportableHistory();
    Task<GeminiResponse> AskAsync(string prompt, AppSettings settings, CancellationToken cancellationToken = default, string? modelOverride = null, List<(byte[] data, string mimeType)>? images = null);
    Task<string> GeneratePowerShellScriptAsync(string intent, AppSettings settings, CancellationToken cancellationToken = default);
    Task<List<string>> FetchAvailableModelsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<string> GenerateWidgetXamlAsync(string description, AppSettings settings, CancellationToken cancellationToken = default);
}
