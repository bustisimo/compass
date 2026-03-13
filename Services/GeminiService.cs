using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Compass.Services.Interfaces;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class GeminiResponse
{
    public string Text { get; set; } = "";
    public string ModelUsed { get; set; } = "";
    public List<(byte[] data, string mimeType)> Images { get; set; } = new();
}

public class GeminiService : IGeminiService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly List<Content> _chatHistory = new();
    private readonly ILogger<GeminiService> _logger;
    private const int MaxToolRounds = 5;

    // WQL classes considered safe for read-only system info queries
    private static readonly HashSet<string> AllowedWmiClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Win32_Processor", "Win32_ComputerSystem", "Win32_OperatingSystem",
        "Win32_PhysicalMemory", "Win32_DiskDrive", "Win32_LogicalDisk",
        "Win32_NetworkAdapter", "Win32_NetworkAdapterConfiguration",
        "Win32_VideoController", "Win32_BaseBoard", "Win32_BIOS",
        "Win32_Battery", "Win32_Fan", "Win32_TemperatureProbe",
        "Win32_Process", "Win32_Service", "Win32_StartupCommand",
        "Win32_TimeZone", "Win32_Desktop", "Win32_PerfFormattedData_PerfOS_Processor",
        "Win32_PerfFormattedData_PerfOS_Memory", "Win32_PerfFormattedData_PerfDisk_PhysicalDisk",
        "Win32_SoundDevice", "Win32_Printer", "Win32_PointingDevice",
        "Win32_Keyboard", "Win32_Volume", "Win32_UserAccount",
        "Win32_SystemEnclosure", "Win32_PnPEntity"
    };

    // WQL keywords that indicate write/destructive operations
    private static readonly Regex DangerousWqlPattern = new(
        @"\b(DELETE|UPDATE|CREATE|DROP|INSERT|EXEC|ExecMethod)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageGenerationPattern = new(
        @"\b(generate an? (image|picture|photo|artwork|illustration|graphic|icon|logo|wallpaper|poster|banner)" +
        @"|create an? (image|picture|photo|artwork|illustration|graphic|icon|logo|wallpaper|poster|banner)" +
        @"|make an? (image|picture|photo|artwork|illustration|graphic|icon|logo|wallpaper|poster|banner)" +
        @"|render an? (image|picture|photo|artwork|illustration)" +
        @"|draw (me |a |an |the )?" +
        @"|paint (me |a |an |the )?" +
        @"|sketch (me |a |an |the )?" +
        @"|illustrate\b" +
        @"|design an? (image|logo|icon|poster|banner|graphic)" +
        @"|visualize\b" +
        @"|show me what .+ looks? like" +
        @"|picture of\b" +
        @"|photo of\b" +
        @"|image of\b" +
        @"|can you (draw|paint|sketch|create|generate|make|design|render)" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly PowerShellSandbox _sandbox;

    public GeminiService(ILogger<GeminiService> logger, PowerShellSandbox sandbox)
    {
        _logger = logger;
        _sandbox = sandbox;
    }

    public bool HasHistory => _chatHistory.Count > 0;

    public void ClearHistory() => _chatHistory.Clear();

    public void RemoveLastExchange()
    {
        // Walk backwards and remove trailing model/function entries
        while (_chatHistory.Count > 0)
        {
            var last = _chatHistory[^1];
            if (last.Role is "model" or "function")
                _chatHistory.RemoveAt(_chatHistory.Count - 1);
            else
                break;
        }
        // Remove the last user entry
        if (_chatHistory.Count > 0 && _chatHistory[^1].Role == "user")
            _chatHistory.RemoveAt(_chatHistory.Count - 1);
    }

    public string SerializeHistory()
    {
        var entries = new List<Dictionary<string, object>>();
        foreach (var entry in _chatHistory)
        {
            string? text = entry.Parts?.FirstOrDefault(p => p.Text != null)?.Text;
            if (string.IsNullOrWhiteSpace(text) && entry.Parts?.Any(p => p.InlineData?.Data != null) != true)
                continue;

            var dict = new Dictionary<string, object>
            {
                { "role", entry.Role ?? "user" },
                { "text", text ?? "" }
            };

            // Save inline image data as base64
            var images = entry.Parts?
                .Where(p => p.InlineData?.Data != null && p.InlineData.Data.Length > 0)
                .Select(p => new Dictionary<string, string>
                {
                    { "data", Convert.ToBase64String(p.InlineData!.Data!) },
                    { "mimeType", p.InlineData.MimeType ?? "image/png" }
                })
                .ToList();

            if (images != null && images.Count > 0)
                dict["images"] = images;

            entries.Add(dict);
        }
        return JsonSerializer.Serialize(entries);
    }

    public void LoadSerializedHistory(string json)
    {
        _chatHistory.Clear();
        var entries = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
        if (entries == null) return;
        foreach (var entry in entries)
        {
            string role = entry.TryGetValue("role", out var r) ? r.GetString() ?? "user" : "user";
            string text = entry.TryGetValue("text", out var t) ? t.GetString() ?? "" : "";

            var parts = new List<Part>();
            if (!string.IsNullOrEmpty(text))
                parts.Add(new Part { Text = text });

            // Restore inline image data
            if (entry.TryGetValue("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var imgEl in imagesEl.EnumerateArray())
                {
                    string? b64 = imgEl.TryGetProperty("data", out var d) ? d.GetString() : null;
                    string mime = imgEl.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "image/png" : "image/png";
                    if (!string.IsNullOrEmpty(b64))
                    {
                        parts.Add(new Part
                        {
                            InlineData = new Blob { Data = Convert.FromBase64String(b64), MimeType = mime }
                        });
                    }
                }
            }

            if (parts.Count > 0)
                _chatHistory.Add(new Content { Role = role, Parts = parts });
        }
    }

    public List<(string role, string text, List<(byte[] data, string mimeType)> images)> GetExportableHistoryWithImages()
    {
        var result = new List<(string, string, List<(byte[], string)>)>();
        foreach (var entry in _chatHistory)
        {
            if (entry.Role == "function") continue;
            string? text = entry.Parts?.FirstOrDefault(p => p.Text != null)?.Text;
            var images = entry.Parts?
                .Where(p => p.InlineData?.Data != null && p.InlineData.Data.Length > 0)
                .Select(p => (p.InlineData!.Data!, p.InlineData.MimeType ?? "image/png"))
                .ToList() ?? new();

            if (string.IsNullOrWhiteSpace(text) && images.Count == 0) continue;

            string role = entry.Role switch
            {
                "user" => "You",
                "model" => "Compass",
                _ => entry.Role ?? "Unknown"
            };
            result.Add((role, text ?? "", images));
        }
        return result;
    }

    public List<(string role, string text)> GetExportableHistory()
    {
        return GetExportableHistoryWithImages()
            .Select(e => (e.role, e.text))
            .ToList();
    }

    public async Task<GeminiResponse> AskAsync(
        string prompt,
        AppSettings settings,
        CancellationToken cancellationToken = default,
        string? modelOverride = null,
        List<(byte[] data, string mimeType)>? images = null)
    {
        string model = modelOverride ?? settings.SelectedModel;

        // Build user content parts
        var userParts = new List<Part>();

        // Add image parts first (if any)
        if (images != null)
        {
            foreach (var (data, mimeType) in images)
            {
                userParts.Add(new Part
                {
                    InlineData = new Blob
                    {
                        MimeType = mimeType,
                        Data = data
                    }
                });
            }
        }

        userParts.Add(new Part { Text = prompt });

        _chatHistory.Add(new Content
        {
            Role = "user",
            Parts = userParts
        });

        // Check if this is an image generation request
        bool isImageGen = IsImageGenerationRequest(prompt);
        string imageGenModel = settings.ImageGenerationModel;

        var wmiTool = BuildTools();
        object requestBody;

        if (isImageGen)
        {
            // Image models work best with a single-turn request — no chat history,
            // no system instruction, no tools. Send only the current user message.
            var imageContent = new Content
            {
                Role = "user",
                Parts = userParts
            };
            requestBody = new
            {
                contents = new[] { imageContent },
                generationConfig = new { responseModalities = new[] { "TEXT", "IMAGE" } }
            };
            model = imageGenModel;
        }
        else
        {
            requestBody = new
            {
                contents = _chatHistory,
                systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
                tools = new[] { wmiTool }
            };
        }

        var response = await ExecuteRequest(requestBody, settings, cancellationToken, model);

        // Multi-round tool calling loop (up to MaxToolRounds)
        if (!isImageGen)
        {
            for (int round = 0; round < MaxToolRounds; round++)
            {
                var candidate = response?.Candidates?.FirstOrDefault();
                var functionCallParts = candidate?.Content?.Parts?
                    .Where(p => p.FunctionCall != null)
                    .ToList();

                if (functionCallParts == null || functionCallParts.Count == 0)
                    break;

                // Add the model's response (with function calls) to history
                if (candidate?.Content != null)
                    _chatHistory.Add(candidate.Content);

                // Execute all function calls
                var functionResponseParts = ExecuteFunctionCalls(
                    functionCallParts.Select(p => p.FunctionCall!));

                if (functionResponseParts.Count == 0)
                    break;

                _chatHistory.Add(new Content
                {
                    Role = "function",
                    Parts = functionResponseParts
                });

                var followUpBody = new
                {
                    contents = _chatHistory,
                    systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
                    tools = new[] { wmiTool }
                };
                response = await ExecuteRequest(followUpBody, settings, cancellationToken, model);
            }
        }

        // Parse response
        var finalCandidate = response?.Candidates?.FirstOrDefault();
        var result = new GeminiResponse { ModelUsed = model };

        if (finalCandidate?.Content?.Parts != null)
        {
            foreach (var part in finalCandidate.Content.Parts)
            {
                if (part.Text != null)
                    result.Text += part.Text;

                if (part.InlineData?.Data != null && part.InlineData.Data.Length > 0)
                {
                    result.Images.Add((part.InlineData.Data, part.InlineData.MimeType ?? "image/png"));
                }
            }
        }

        // Add text response to history
        if (!string.IsNullOrEmpty(result.Text))
        {
            _chatHistory.Add(new Content
            {
                Role = "model",
                Parts = new List<Part> { new Part { Text = result.Text } }
            });
        }

        if (string.IsNullOrWhiteSpace(result.Text) && result.Images.Count == 0)
            result.Text = "I received an empty response from the API. Please try rephrasing your question.";

        return result;
    }

    // ---------------------------------------------------------------------------
    // Streaming API
    // ---------------------------------------------------------------------------

    public async Task<GeminiResponse> AskStreamingAsync(
        string prompt,
        AppSettings settings,
        Action<string> onChunk,
        CancellationToken cancellationToken = default,
        string? modelOverride = null)
    {
        string model = modelOverride ?? settings.SelectedModel;

        var userParts = new List<Part> { new Part { Text = prompt } };
        _chatHistory.Add(new Content { Role = "user", Parts = userParts });

        var wmiTool = BuildTools();
        var requestBody = new
        {
            contents = _chatHistory,
            systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
            tools = new[] { wmiTool }
        };

        // First call — try streaming
        var (streamedText, functionCalls, rawParts) = await ExecuteStreamingRequest(requestBody, settings, onChunk, cancellationToken, model);

        // If function calls were returned, handle them non-streaming then stream the follow-up
        if (functionCalls.Count > 0)
        {
            // Add the model's original response parts to history (preserves thought_signature)
            _chatHistory.Add(new Content { Role = "model", Parts = rawParts });

            for (int round = 0; round < MaxToolRounds; round++)
            {
                var functionResponseParts = ExecuteFunctionCalls(functionCalls);

                if (functionResponseParts.Count == 0) break;

                _chatHistory.Add(new Content { Role = "function", Parts = functionResponseParts });

                var followUpBody = new
                {
                    contents = _chatHistory,
                    systemInstruction = new { parts = new[] { new { text = settings.SystemPrompt } } },
                    tools = new[] { wmiTool }
                };

                // Stream the follow-up response
                var (followUpText, followUpCalls, followUpRawParts) = await ExecuteStreamingRequest(followUpBody, settings, onChunk, cancellationToken, model);
                streamedText = followUpText;
                functionCalls = followUpCalls;
                rawParts = followUpRawParts;

                if (functionCalls.Count == 0) break;

                // More function calls — add original parts to history (preserves thought_signature)
                _chatHistory.Add(new Content { Role = "model", Parts = rawParts });
            }
        }

        // Add final response to history
        if (!string.IsNullOrEmpty(streamedText))
        {
            _chatHistory.Add(new Content
            {
                Role = "model",
                Parts = new List<Part> { new Part { Text = streamedText } }
            });
        }

        var result = new GeminiResponse { ModelUsed = model, Text = streamedText };
        if (string.IsNullOrWhiteSpace(result.Text))
            result.Text = "I received an empty response from the API. Please try rephrasing your question.";

        return result;
    }

    private static async Task<(string text, List<FunctionCall> functionCalls, List<Part> rawModelParts)> ExecuteStreamingRequest(
        object requestBody,
        AppSettings settings,
        Action<string> onChunk,
        CancellationToken cancellationToken,
        string model)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("API key is not configured.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?key={settings.ApiKey}&alt=sse";
        var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"API error {response.StatusCode}: {errText}");
        }

        var fullText = new StringBuilder();
        var functionCalls = new List<FunctionCall>();
        var rawModelParts = new List<Part>();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            if (!line.StartsWith("data: ")) continue;
            var jsonData = line.Substring(6).Trim();
            if (string.IsNullOrEmpty(jsonData)) continue;

            try
            {
                var chunk = JsonSerializer.Deserialize<GenerateContentResponse>(jsonData, _jsonOptions);

                var candidate = chunk?.Candidates?.FirstOrDefault();
                if (candidate?.Content?.Parts == null) continue;

                foreach (var part in candidate.Content.Parts)
                {
                    // Preserve the original part (includes thought_signature etc.)
                    rawModelParts.Add(part);

                    if (part.Text != null)
                    {
                        fullText.Append(part.Text);
                        onChunk(part.Text);
                    }
                    if (part.FunctionCall != null)
                    {
                        functionCalls.Add(part.FunctionCall);
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE chunks
            }
        }

        return (fullText.ToString(), functionCalls, rawModelParts);
    }

    private static readonly string PowerShellSystemPrompt = @"You are a Windows automation expert specializing in PowerShell scripting. Write a safe, functional PowerShell script for the user's request.

Return ONLY the raw script text. No markdown formatting, no backticks, no explanations.

You have access to the full PowerShell ecosystem and Win32 API via Add-Type. Common patterns you should use:

## Launching Applications
- Use Start-Process for launching apps: Start-Process ""C:\Program Files\...\app.exe""
- Common app paths:
  - Discord: ""$env:LOCALAPPDATA\Discord\Update.exe"" --processStart Discord.exe
  - Steam: ""C:\Program Files (x86)\Steam\steam.exe""
  - Chrome: ""C:\Program Files\Google\Chrome\Application\chrome.exe""
  - Firefox: ""C:\Program Files\Mozilla Firefox\firefox.exe""
  - Spotify: ""$env:APPDATA\Spotify\Spotify.exe""
  - VS Code: ""$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe""
  - Windows Terminal: wt.exe
  - File Explorer: explorer.exe
  - Notepad: notepad.exe
- For apps not in known paths, try: Start-Process ""app-name"" or search via Get-Command

## Window Positioning with Win32 API
When the user asks to position windows on specific monitors, use this pattern:

```
Add-Type @""
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport(""user32.dll"")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport(""user32.dll"")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport(""user32.dll"")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
}
""@

# Enumerate monitors
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }
$monitor1 = $screens[0].WorkingArea
$monitor2 = if ($screens.Count -gt 1) { $screens[1].WorkingArea } else { $screens[0].WorkingArea }
```

## Waiting for Windows to Load
After launching an app, wait for its main window before positioning:
```
$proc = Start-Process ""app.exe"" -PassThru
$timeout = 30
$elapsed = 0
while ($proc.MainWindowHandle -eq [IntPtr]::Zero -and $elapsed -lt $timeout) {
    Start-Sleep -Milliseconds 500
    $elapsed += 0.5
    $proc.Refresh()
}
```

## Window Positioning Patterns
- Snap left on monitor: MoveWindow($hwnd, $monitor.X, $monitor.Y, $monitor.Width / 2, $monitor.Height, $true)
- Snap right on monitor: MoveWindow($hwnd, $monitor.X + $monitor.Width / 2, $monitor.Y, $monitor.Width / 2, $monitor.Height, $true)
- Maximize on monitor: Move window to target monitor first, then ShowWindow($hwnd, 3) (SW_MAXIMIZE = 3)
- Full screen: MoveWindow($hwnd, $monitor.X, $monitor.Y, $monitor.Width, $monitor.Height, $true)
- SW_RESTORE = 9, SW_MINIMIZE = 6, SW_MAXIMIZE = 3, SW_SHOW = 5

## Important Notes
- Always use -PassThru with Start-Process when you need the process handle
- Always call $proc.Refresh() inside the wait loop to update MainWindowHandle
- Handle the case where an app is already running (Get-Process -Name ""app"" -ErrorAction SilentlyContinue)
- Use try/catch for robustness
- Do NOT use Read-Host or any interactive prompts
- Do NOT modify system settings destructively
- Scripts should be idempotent when possible";

    public async Task<string> GeneratePowerShellScriptAsync(string intent, AppSettings settings, CancellationToken cancellationToken = default)
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
                parts = new[] { new { text = PowerShellSystemPrompt } }
            }
        };

        var response = await ExecuteRequest(requestBody, settings, cancellationToken);
        string? script = response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException("The AI returned an empty response. Please try a more specific description.");

        return script.Replace("```powershell", "").Replace("```", "").Trim();
    }

    public async Task<string> GenerateWidgetXamlAsync(string description, AppSettings settings, CancellationToken cancellationToken = default)
    {
        string systemPrompt = $@"You are a WPF XAML widget designer. Generate a compact, visually appealing WPF XAML snippet for a widget.

STRICT RULES:
- Root element must be Border, StackPanel, or Grid
- Only use visual elements: TextBlock, Path, Rectangle, Ellipse, ProgressBar, Border, StackPanel, Grid, Canvas
- NO event handlers (no Click=, no MouseDown=, etc.)
- NO Binding expressions
- NO StaticResource or DynamicResource references
- NO x:Class attribute
- NO clr-namespace imports
- Use inline colors only (hex codes like #FF4CC2FF)
- Max height ~150px, compact layout
- Return ONLY the raw XAML, no markdown, no backticks, no explanation

Current theme colors for consistency:
- Primary/Background: {settings.PrimaryColor}
- Accent: {settings.AccentColor}
- Text: {(string.IsNullOrEmpty(settings.TextColor) ? "#FFFFFF" : settings.TextColor)}
- Border: {settings.BorderColor}
- Font: {settings.FontFamily}";

        var userContent = new Content
        {
            Role = "user",
            Parts = new List<Part> { new Part { Text = description } }
        };

        var requestBody = new
        {
            contents = new[] { userContent },
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } }
        };

        var response = await ExecuteRequest(requestBody, settings, cancellationToken);
        string? xaml = response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(xaml))
            throw new InvalidOperationException("The AI returned an empty response. Please try a more specific description.");

        return xaml.Replace("```xml", "").Replace("```xaml", "").Replace("```", "").Trim();
    }

    public async Task<List<string>> FetchAvailableModelsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return new List<string>();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={settings.ApiKey}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to fetch models ({response.StatusCode}): {errText}");
        }

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

            // Filter by description keywords indicating unavailability
            if (m.TryGetProperty("description", out var descProp))
            {
                string desc = descProp.GetString() ?? "";
                if (desc.Contains("discontinued", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("deprecated", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("no longer available", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("shut down", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("decommission", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (!m.TryGetProperty("name", out var nameProp)) continue;
            string name = nameProp.GetString() ?? "";
            if (name.StartsWith("models/")) name = name.Substring(7);

            if (IsUnavailableModel(name))
                continue;

            list.Add(name);
        }
        return list.OrderByDescending(x => x).ToList();
    }

    /// <summary>
    /// Determines whether a model name refers to a known unavailable, legacy, or
    /// non-conversational model that should be hidden from the user's model list.
    /// </summary>
    private static bool IsUnavailableModel(string name)
    {
        // Legacy model families (sunset or replaced)
        if (name.StartsWith("gemini-1.0") ||
            name.StartsWith("gemini-pro") ||
            name.StartsWith("chat-") ||
            name.StartsWith("text-"))
            return true;

        // Non-conversational / utility models
        if (name.Contains("aqa") ||
            name.Contains("embedding") ||
            name.Contains("bisheng") ||
            name.Contains("tuning"))
            return true;

        // Gemini 1.5 models — filter out versions with fixed date suffixes
        // (e.g., gemini-1.5-flash-001, gemini-1.5-pro-002) as these are
        // point-in-time snapshots that get sunset. Keep only the latest
        // aliases (e.g., "gemini-1.5-flash-latest") if they still exist.
        if (name.StartsWith("gemini-1.5"))
        {
            // Allow "latest" aliases through since they auto-resolve
            if (name.EndsWith("-latest"))
                return false;
            // Block fixed-version 1.5 snapshots (they get decommissioned)
            return true;
        }

        // Gemini 2.0 — entire family superseded by 2.5
        if (name.StartsWith("gemini-2.0"))
            return true;

        // Block any remaining dated snapshots (e.g., -001, -002, -YYYY-MM-DD suffix)
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"-\d{3}$"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"-\d{4}-\d{2}-\d{2}$"))
            return true;

        return false;
    }

    public async Task<(byte[] data, string mimeType)?> GenerateImageAsync(
        string prompt,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userContent = new Content
            {
                Role = "user",
                Parts = new List<Part> { new Part { Text = prompt } }
            };

            var requestBody = new
            {
                contents = new[] { userContent },
                generationConfig = new { responseModalities = new[] { "TEXT", "IMAGE" } }
            };

            string model = settings.ImageGenerationModel;
            var response = await ExecuteRequest(requestBody, settings, cancellationToken, model);

            var candidate = response?.Candidates?.FirstOrDefault();
            if (candidate?.Content?.Parts != null)
            {
                foreach (var part in candidate.Content.Parts)
                {
                    if (part.InlineData?.Data != null && part.InlineData.Data.Length > 0)
                    {
                        return (part.InlineData.Data, part.InlineData.MimeType ?? "image/png");
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image generation failed for prompt: {Prompt}", prompt);
            return null;
        }
    }

    public static bool IsImageGenerationRequest(string prompt)
    {
        return ImageGenerationPattern.IsMatch(prompt);
    }

    private static async Task<GenerateContentResponse?> ExecuteRequest(
        object requestBody,
        AppSettings settings,
        CancellationToken cancellationToken = default,
        string? modelOverride = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("API key is not configured.");

        string model = modelOverride ?? settings.SelectedModel;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.ApiKey}";
        var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"API error {response.StatusCode}: {errText}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GenerateContentResponse>(stream, _jsonOptions, cancellationToken);
    }

    private List<Part> ExecuteFunctionCalls(IEnumerable<FunctionCall> functionCalls)
    {
        var responseParts = new List<Part>();
        foreach (var fc in functionCalls)
        {
            if (fc.Name == "execute_wmi_query")
            {
                object? queryObj = null;
                fc.Args?.TryGetValue("query", out queryObj);
                string wmiResult = string.IsNullOrWhiteSpace(queryObj?.ToString())
                    ? "Error: No query provided."
                    : ExecuteWmiQuery(queryObj.ToString()!);
                responseParts.Add(new Part
                {
                    FunctionResponse = new FunctionResponse
                    {
                        Name = "execute_wmi_query",
                        Response = new Dictionary<string, object> { { "result", wmiResult } }
                    }
                });
            }
            else if (fc.Name == "execute_powershell")
            {
                object? scriptObj = null;
                fc.Args?.TryGetValue("script", out scriptObj);
                string psResult = string.IsNullOrWhiteSpace(scriptObj?.ToString())
                    ? "Error: No script provided."
                    : _sandbox.Execute(scriptObj.ToString()!);
                responseParts.Add(new Part
                {
                    FunctionResponse = new FunctionResponse
                    {
                        Name = "execute_powershell",
                        Response = new Dictionary<string, object> { { "result", psResult } }
                    }
                });
            }
        }
        return responseParts;
    }

    private static Tool BuildTools() => new Tool
    {
        FunctionDeclarations = new List<FunctionDeclaration>
        {
            new FunctionDeclaration
            {
                Name = "execute_wmi_query",
                Description = "Executes a WMI query to retrieve Windows system/hardware data. " +
                    "Call this whenever the user's question relates to their system, hardware, performance, PC health, or could benefit from real data — " +
                    "even for indirect questions like 'should I reboot?', 'is my PC slow?', 'how's my computer doing?'. " +
                    "Do NOT call this for general conversation, greetings, or non-system questions. " +
                    "For system health or reboot questions, ALWAYS query ALL of these (issue multiple calls): " +
                    "1) Win32_Processor — CPU load (result includes multi-sample average). " +
                    "2) Win32_OperatingSystem — CRITICAL: always query LastBootUpTime (uptime), TotalVisibleMemorySize, FreePhysicalMemory. " +
                    "3) Win32_LogicalDisk — disk free space. " +
                    "4) Win32_VideoController — GPU info. " +
                    "When analyzing results, think critically and give actionable advice: " +
                    "uptime over 3-4 days generally warrants a reboot for Windows stability and memory leaks; " +
                    "RAM usage over 80% suggests too many processes or a memory leak; " +
                    "sustained CPU over 50% at idle needs investigation. " +
                    "Don't just list numbers — interpret them, flag concerns, and make a clear recommendation.",
                Parameters = new Schema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Schema>
                    {
                        { "query", new Schema { Type = "string", Description = "The WQL query string to execute (e.g., 'SELECT LoadPercentage FROM Win32_Processor')." } }
                    },
                    Required = new List<string> { "query" }
                }
            },
            new FunctionDeclaration
            {
                Name = "execute_powershell",
                Description = "Executes a PowerShell script on the user's Windows system. " +
                    "Use this when the user asks you to perform actions, automate tasks, manage files, or run system commands. " +
                    "The script runs in ConstrainedLanguage mode with safety checks. " +
                    "Do NOT use this for simple conversation or questions that don't require system interaction.",
                Parameters = new Schema
                {
                    Type = "object",
                    Properties = new Dictionary<string, Schema>
                    {
                        { "script", new Schema { Type = "string", Description = "The PowerShell script to execute." } }
                    },
                    Required = new List<string> { "script" }
                }
            }
        }
    };

    private string ExecuteWmiQuery(string query)
    {
        // Reject queries containing dangerous WQL keywords
        if (DangerousWqlPattern.IsMatch(query))
            return "Error: Query rejected — only read-only SELECT queries are allowed.";

        // Validate the FROM class is in the allowlist
        var fromMatch = Regex.Match(query, @"\bFROM\s+(\w+)", RegexOptions.IgnoreCase);
        if (fromMatch.Success)
        {
            string className = fromMatch.Groups[1].Value;
            if (!AllowedWmiClasses.Contains(className))
                return $"Error: WMI class '{className}' is not in the allowed list. Only standard Win32_* system info classes are permitted.";
        }

        // CPU LoadPercentage: take multiple samples and average for a useful reading
        if (IsCpuLoadQuery(query))
            return ExecuteCpuLoadAverage();

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
            _logger.LogError(ex, "WMI query failed: {Query}", query);
            return $"Error executing query: {ex.Message}";
        }
    }

    private static bool IsCpuLoadQuery(string query)
    {
        return Regex.IsMatch(query, @"\bLoadPercentage\b", RegexOptions.IgnoreCase) &&
               Regex.IsMatch(query, @"\bWin32_Processor\b", RegexOptions.IgnoreCase);
    }

    private static string ExecuteCpuLoadAverage()
    {
        try
        {
            const int samples = 5;
            const int intervalMs = 400;
            var readings = new List<int>();

            for (int i = 0; i < samples; i++)
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT LoadPercentage FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (System.Management.ManagementBaseObject obj in collection)
                {
                    if (obj["LoadPercentage"] is ushort load)
                        readings.Add(load);
                }
                if (i < samples - 1)
                    Thread.Sleep(intervalMs);
            }

            if (readings.Count == 0)
                return "Error: Could not read CPU load.";

            double average = readings.Average();
            int min = readings.Min();
            int max = readings.Max();
            int current = readings.Last();

            var result = new Dictionary<string, object>
            {
                { "AverageLoadPercentage", Math.Round(average, 1) },
                { "CurrentLoadPercentage", current },
                { "MinLoadPercentage", min },
                { "MaxLoadPercentage", max },
                { "SampleCount", readings.Count },
                { "SampleIntervalMs", intervalMs }
            };
            return JsonSerializer.Serialize(new[] { result });
        }
        catch (Exception ex)
        {
            return $"Error measuring CPU load: {ex.Message}";
        }
    }
}
