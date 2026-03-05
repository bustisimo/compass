using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.GenAI;
using Google.GenAI.Types;

namespace Compass.Services;

public class GeminiService
{
    private static readonly HttpClient _httpClient = new();
    private readonly List<Content> _chatHistory = new();

    public bool HasHistory => _chatHistory.Count > 0;

    public void ClearHistory() => _chatHistory.Clear();

    /// <summary>
    /// Sends a user message, handles WMI tool calls, maintains history.
    /// Returns the model's response text. Throws on network/API failure.
    /// </summary>
    public async Task<string> AskAsync(string prompt, AppSettings settings)
    {
        _chatHistory.Add(new Content
        {
            Role = "user",
            Parts = new List<Part> { new Part { Text = prompt } }
        });

        var wmiTool = BuildWmiTool();
        var requestBody = new
        {
            contents = _chatHistory,
            systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
            tools = new[] { wmiTool }
        };

        var response = await ExecuteRequest(requestBody, settings);

        // Handle function calls (one round supported)
        var candidate = response?.Candidates?.FirstOrDefault();
        var functionCallPart = candidate?.Content?.Parts?.FirstOrDefault(p => p.FunctionCall != null);

        if (functionCallPart?.FunctionCall?.Name == "execute_wmi_query")
        {
            var functionCall = functionCallPart.FunctionCall;
            object? queryObj = null;
            functionCall.Args?.TryGetValue("query", out queryObj);
            string? wqlQuery = queryObj?.ToString();

            if (!string.IsNullOrWhiteSpace(wqlQuery))
            {
                if (candidate?.Content != null)
                    _chatHistory.Add(candidate.Content);

                string wmiResult = ExecuteWmiQuery(wqlQuery);

                _chatHistory.Add(new Content
                {
                    Role = "function",
                    Parts = new List<Part>
                    {
                        new Part
                        {
                            FunctionResponse = new FunctionResponse
                            {
                                Name = "execute_wmi_query",
                                Response = new Dictionary<string, object> { { "result", wmiResult } }
                            }
                        }
                    }
                });

                var requestBody2 = new
                {
                    contents = _chatHistory,
                    systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
                    tools = new[] { wmiTool }
                };
                response = await ExecuteRequest(requestBody2, settings);
                candidate = response?.Candidates?.FirstOrDefault();
            }
        }

        string? text = candidate?.Content?.Parts?.FirstOrDefault(p => p.Text != null)?.Text;

        if (text != null)
        {
            _chatHistory.Add(new Content
            {
                Role = "model",
                Parts = new List<Part> { new Part { Text = text } }
            });
        }

        return text ?? "...";
    }

    /// <summary>
    /// Shared PowerShell generation used by both chat commands and the manager panel.
    /// Returns cleaned script text, or null on failure.
    /// </summary>
    public async Task<string?> GeneratePowerShellScriptAsync(string intent, AppSettings settings)
    {
        var userContent = new Content
        {
            Role = "user",
            Parts = new List<Part> { new Part { Text = intent } }
        };

        var requestBody = new
        {
            contents = new[] { userContent },
            systemInstruction = new
            {
                parts = new[] { new { text = "You are a Windows automation expert. The user wants to: " + intent + ". Write a safe, functional PowerShell script to do this. Return ONLY the raw script text. No markdown formatting, no backticks, no explanations." } }
            }
        };

        var response = await ExecuteRequest(requestBody, settings);
        string? script = response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (script != null)
            script = script.Replace("```powershell", "").Replace("```", "").Trim();
        return script;
    }

    /// <summary>Returns available generateContent-capable models, sorted descending.</summary>
    public async Task<List<string>> FetchAvailableModelsAsync(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return new List<string>();
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={settings.ApiKey}";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return new List<string>();

            var list = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                bool canGenerate = false;
                if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                    foreach (var method in methods.EnumerateArray())
                        if (method.ValueEquals("generateContent")) { canGenerate = true; break; }

                if (m.TryGetProperty("description", out var descProp))
                {
                    string desc = descProp.GetString() ?? "";
                    if (desc.Contains("discontinued", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("deprecated", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (canGenerate && m.TryGetProperty("name", out var nameProp))
                {
                    string name = nameProp.GetString() ?? "";
                    if (name.StartsWith("models/")) name = name.Substring(7);
                    list.Add(name);
                }
            }
            return list.OrderByDescending(x => x).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Compass] FetchAvailableModels: {ex.Message}");
            return new List<string>();
        }
    }

    private static async Task<GenerateContentResponse?> ExecuteRequest(object requestBody, AppSettings settings)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{settings.SelectedModel}:generateContent?key={settings.ApiKey}";
        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"API error {response.StatusCode}: {errText}");
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GenerateContentResponse>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static Tool BuildWmiTool() => new Tool
    {
        FunctionDeclarations = new List<FunctionDeclaration>
        {
            new FunctionDeclaration
            {
                Name = "execute_wmi_query",
                Description = "Executes a WMI (Windows Management Instrumentation) query to retrieve system hardware, performance, and configuration data.",
                Parameters = new Schema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Schema>
                    {
                        { "query", new Schema { Type = "string", Description = "The WQL query string to execute (e.g., 'SELECT LoadPercentage FROM Win32_Processor')." } }
                    },
                    Required = new List<string> { "query" }
                }
            }
        }
    };

    private static string ExecuteWmiQuery(string query)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(query);
            using var collection = searcher.Get();
            var results = new List<Dictionary<string, object>>();
            foreach (System.Management.ManagementBaseObject obj in collection)
            {
                var item = new Dictionary<string, object>();
                foreach (System.Management.PropertyData prop in obj.Properties)
                    if (prop.Value != null) item[prop.Name] = prop.Value;
                results.Add(item);
            }
            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            return $"Error executing query: {ex.Message}";
        }
    }
}
