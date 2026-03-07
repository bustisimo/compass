namespace Compass;

public class TextChunk
{
    public string FilePath { get; set; } = "";
    public string Content { get; set; } = "";
    public int StartLine { get; set; }
    public double Score { get; set; }
}
