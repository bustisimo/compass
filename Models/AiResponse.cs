namespace Compass;

public class AiResponse
{
    public string Text { get; set; } = "";
    public string ModelUsed { get; set; } = "";
    public List<(byte[] Data, string MimeType)> Images { get; set; } = new();
    public List<AiFunctionCall>? FunctionCalls { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Images.Count == 0;
}
