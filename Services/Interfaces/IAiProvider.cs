namespace Compass.Services.Interfaces;

public interface IAiProvider
{
    string Name { get; }
    bool SupportsToolCalling { get; }
    bool SupportsImageGeneration { get; }
    Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default);
    Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default);
}
