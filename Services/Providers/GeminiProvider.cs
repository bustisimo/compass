using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Compass.Services.Interfaces;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;

namespace Compass.Services.Providers;

public class GeminiProvider : IAiProvider
{
    private static readonly HttpClient _httpClient = new();
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(ILogger<GeminiProvider> logger) => _logger = logger;

    public string Name => "Gemini";
    public bool SupportsToolCalling => true;
    public bool SupportsImageGeneration => true;

    public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new InvalidOperationException("API key is not configured.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{request.Model}:generateContent?key={request.ApiKey}";

        var requestBody = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Gemini API error {response.StatusCode}: {errText}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var geminiResponse = await JsonSerializer.DeserializeAsync<GenerateContentResponse>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }, cancellationToken);

        return ParseResponse(geminiResponse, request.Model);
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new List<string>();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new List<string>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var models)) return new List<string>();

        var list = new List<string>();
        foreach (var m in models.EnumerateArray())
        {
            bool canGenerate = false;
            if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                foreach (var method in methods.EnumerateArray())
                    if (method.ValueEquals("generateContent")) { canGenerate = true; break; }

            if (!canGenerate) continue;

            if (m.TryGetProperty("description", out var descProp))
            {
                string desc = descProp.GetString() ?? "";
                if (desc.Contains("discontinued", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("deprecated", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (!m.TryGetProperty("name", out var nameProp)) continue;
            string name = nameProp.GetString() ?? "";
            if (name.StartsWith("models/")) name = name.Substring(7);

            if (name.StartsWith("gemini-1.0") || name.StartsWith("gemini-1.5") ||
                name.StartsWith("gemini-pro") || name.Contains("aqa") ||
                name.Contains("embedding") || name.Contains("bisheng"))
                continue;

            list.Add(name);
        }
        return list.OrderByDescending(x => x).ToList();
    }

    private static object BuildRequestBody(AiRequest request)
    {
        var contents = new List<object>();

        // History
        foreach (var entry in request.History)
        {
            contents.Add(new { role = entry.Role, parts = new[] { new { text = entry.Text } } });
        }

        // Current message
        var userParts = new List<object>();
        if (request.Images != null)
        {
            foreach (var (data, mimeType) in request.Images)
            {
                userParts.Add(new { inlineData = new { mimeType, data = Convert.ToBase64String(data) } });
            }
        }
        userParts.Add(new { text = request.Prompt });
        contents.Add(new { role = "user", parts = userParts });

        if (request.RequestImageGeneration)
        {
            return new
            {
                contents,
                systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
                generationConfig = new { responseModalities = new[] { "TEXT", "IMAGE" } }
            };
        }

        return new
        {
            contents,
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } }
        };
    }

    private static AiResponse ParseResponse(GenerateContentResponse? response, string model)
    {
        var result = new AiResponse { ModelUsed = model };
        var candidate = response?.Candidates?.FirstOrDefault();

        if (candidate?.Content?.Parts != null)
        {
            foreach (var part in candidate.Content.Parts)
            {
                if (part.Text != null)
                    result.Text += part.Text;
                if (part.InlineData?.Data != null && part.InlineData.Data.Length > 0)
                    result.Images.Add((part.InlineData.Data, part.InlineData.MimeType ?? "image/png"));
                if (part.FunctionCall != null)
                {
                    result.FunctionCalls ??= new List<AiFunctionCall>();
                    var args = new Dictionary<string, object>();
                    if (part.FunctionCall.Args != null)
                        foreach (var kv in part.FunctionCall.Args)
                            args[kv.Key] = kv.Value;
                    result.FunctionCalls.Add(new AiFunctionCall { Name = part.FunctionCall.Name ?? "", Args = args });
                }
            }
        }

        return result;
    }
}
