using System.Net.Http;
using System.Text;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services.Providers;

public class AnthropicProvider : IAiProvider
{
    private static readonly HttpClient _httpClient = new();
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(ILogger<AnthropicProvider> logger) => _logger = logger;

    public string Name => "Anthropic";
    public bool SupportsToolCalling => true;
    public bool SupportsImageGeneration => false;

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var messages = new List<object>();

        foreach (var entry in request.History)
            messages.Add(new { role = entry.Role == "model" ? "assistant" : entry.Role, content = entry.Text });

        if (request.Images != null && request.Images.Count > 0)
        {
            var contentParts = new List<object>();
            foreach (var (data, mimeType) in request.Images)
            {
                contentParts.Add(new
                {
                    type = "image",
                    source = new { type = "base64", media_type = mimeType, data = Convert.ToBase64String(data) }
                });
            }
            contentParts.Add(new { type = "text", text = request.Prompt });
            messages.Add(new { role = "user", content = contentParts });
        }
        else
        {
            messages.Add(new { role = "user", content = request.Prompt });
        }

        var body = new
        {
            model = request.Model,
            max_tokens = 4096,
            system = request.SystemPrompt,
            messages
        };

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", request.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = httpContent;

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {errText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        var result = new AiResponse { ModelUsed = request.Model };
        if (doc.RootElement.TryGetProperty("content", out var content))
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    result.Text += text.GetString();
                }
            }
        }

        return result;
    }

    public Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>
        {
            "claude-opus-4-20250514",
            "claude-sonnet-4-20250514",
            "claude-haiku-4-5-20251001"
        });
    }
}
