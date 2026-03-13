using Microsoft.Extensions.Logging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Compass.Services;

public class MediaSessionService
{
    private readonly ILogger<MediaSessionService> _logger;
    private static readonly string _thumbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "compass_albumart.png");

    // Cache the session manager so we don't call RequestAsync() every poll
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private string _lastThumbHash = "";

    public MediaSessionService(ILogger<MediaSessionService> logger)
    {
        _logger = logger;
    }

    public async Task<MediaInfo?> GetCurrentMediaAsync()
    {
        try
        {
            // Cache the session manager — only request once
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            var session = _sessionManager.GetCurrentSession();
            if (session == null) return null;

            var mediaProps = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();

            var title = mediaProps.Title ?? "";
            var artist = mediaProps.Artist ?? "";
            var album = mediaProps.AlbumTitle ?? "";
            var isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
                return null;

            // Check if the song changed
            string hash = $"{title}_{artist}_{album}";
            string thumbFile = "";

            if (hash == _lastThumbHash && System.IO.File.Exists(_thumbPath))
            {
                // Same song — reuse cached thumbnail, skip stream entirely
                thumbFile = _thumbPath;
            }
            else
            {
                // New song — extract thumbnail
                try
                {
                    if (mediaProps.Thumbnail != null)
                    {
                        var streamRef = mediaProps.Thumbnail;
                        using var stream = await streamRef.OpenReadAsync();
                        var size = (uint)stream.Size;
                        if (size > 0)
                        {
                            var buffer = new Windows.Storage.Streams.Buffer(size);
                            await stream.ReadAsync(buffer, size, InputStreamOptions.None);

                            var bytes = new byte[buffer.Length];
                            using var reader = DataReader.FromBuffer(buffer);
                            reader.ReadBytes(bytes);

                            await System.IO.File.WriteAllBytesAsync(_thumbPath, bytes);
                            _lastThumbHash = hash;
                            thumbFile = _thumbPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract album art");
                    // Still return media info without thumbnail
                    if (System.IO.File.Exists(_thumbPath))
                        thumbFile = _thumbPath;
                }
            }

            // Get the source app ID (e.g. "Spotify.exe", "chrome.exe")
            string sourceApp = "";
            try { sourceApp = session.SourceAppUserModelId ?? ""; } catch { }

            return new MediaInfo
            {
                Title = title,
                Artist = artist,
                AlbumTitle = album,
                IsPlaying = isPlaying,
                ThumbnailPath = thumbFile,
                SourceAppId = sourceApp
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get media session info");
            return null;
        }
    }
}
