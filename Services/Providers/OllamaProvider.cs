using System.Net.Http;
using System.Text;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services.Providers;

public class OllamaProvider : IAiProvider
{
    private static readonly HttpClient _httpClient = new();
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(ILogger<OllamaProvider> logger) => _logger = logger;

    public string Name => "Ollama";
    public bool SupportsToolCalling => false;
    public bool SupportsImageGeneration => false;

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        string baseUrl = "http://localhost:11434";

        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        foreach (var entry in request.History)
            messages.Add(new { role = entry.Role == "model" ? "assistant" : entry.Role, content = entry.Text });

        messages.Add(new { role = "user", content = request.Prompt });

        var body = new
        {
            model = request.Model,
            messages,
            stream = false
        };

        var json = JsonSerializer.Serialize(body);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"{baseUrl}/api/chat", httpContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Ollama error {response.StatusCode}: {errText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        var result = new AiResponse { ModelUsed = request.Model };
        if (doc.RootElement.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            result.Text = content.GetString() ?? "";
        }

        return result;
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("http://localhost:11434/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var m in modelsArray.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var name))
                        models.Add(name.GetString() ?? "");
                }
            }
            return models;
        }
        catch
        {
            return new List<string>();
        }
    }
}
