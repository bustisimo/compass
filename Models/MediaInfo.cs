namespace Compass;

public class MediaInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumTitle { get; set; } = "";
    public bool IsPlaying { get; set; }
    public string ThumbnailPath { get; set; } = "";
    public string SourceAppId { get; set; } = "";
}
