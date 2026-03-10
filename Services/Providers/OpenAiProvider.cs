using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services.Providers;

public class OpenAiProvider : IAiProvider
{
    private static readonly HttpClient _httpClient = new();
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(ILogger<OpenAiProvider> logger) => _logger = logger;

    public string Name => "OpenAI";
    public bool SupportsToolCalling => true;
    public bool SupportsImageGeneration => false;

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        foreach (var entry in request.History)
            messages.Add(new { role = entry.Role == "model" ? "assistant" : entry.Role, content = entry.Text });

        if (request.Images != null && request.Images.Count > 0)
        {
            var contentParts = new List<object>();
            foreach (var (data, mimeType) in request.Images)
            {
                contentParts.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{mimeType};base64,{Convert.ToBase64String(data)}" }
                });
            }
            contentParts.Add(new { type = "text", text = request.Prompt });
            messages.Add(new { role = "user", content = contentParts });
        }
        else
        {
            messages.Add(new { role = "user", content = request.Prompt });
        }

        var body = new { model = request.Model, messages };
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        httpRequest.Content = httpContent;

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenAI API error {response.StatusCode}: {errText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        var result = new AiResponse { ModelUsed = request.Model };
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                result.Text = content.GetString() ?? "";
            }
        }

        return result;
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new List<string>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return new List<string>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id))
                {
                    string modelId = id.GetString() ?? "";
                    if (modelId.StartsWith("gpt-"))
                        models.Add(modelId);
                }
            }
        }
        return models.OrderByDescending(x => x).ToList();
    }
}
