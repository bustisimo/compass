namespace Compass;

public class ChatMessage
{
    public string Sender { get; set; } = "";
    public string Text { get; set; } = "";
    public List<byte[]>? Images { get; set; }
    public List<(byte[] data, string mimeType)>? GeneratedImages { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsCode { get; set; }
    public bool IsUser => Sender == "You";
    public bool IsSystem => Sender == "System";
}
