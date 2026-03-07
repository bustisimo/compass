namespace Compass;

public class ClipboardEntry
{
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
}
