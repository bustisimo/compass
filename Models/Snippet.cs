namespace Compass;

public class Snippet
{
    public string Keyword { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int UsageCount { get; set; }
}
