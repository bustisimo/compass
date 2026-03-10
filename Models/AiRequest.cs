namespace Compass;

public class AiRequest
{
    public string Prompt { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<AiChatEntry> History { get; set; } = new();
    public List<(byte[] Data, string MimeType)>? Images { get; set; }
    public bool RequestImageGeneration { get; set; }
    public List<AiToolDefinition>? Tools { get; set; }
}

public class AiChatEntry
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public List<AiFunctionCall>? FunctionCalls { get; set; }
    public List<AiFunctionResponse>? FunctionResponses { get; set; }
}

public class AiFunctionCall
{
    public string Name { get; set; } = "";
    public Dictionary<string, object> Args { get; set; } = new();
}

public class AiFunctionResponse
{
    public string Name { get; set; } = "";
    public Dictionary<string, object> Response { get; set; } = new();
}

public class AiToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, AiToolParameter> Parameters { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class AiToolParameter
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
}
