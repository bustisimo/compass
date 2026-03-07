using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class MediaSessionService
{
    private readonly ILogger<MediaSessionService> _logger;

    public MediaSessionService(ILogger<MediaSessionService> logger)
    {
        _logger = logger;
    }

    public async Task<MediaInfo?> GetCurrentMediaAsync()
    {
        try
        {
            // Use PowerShell to get media info via WinRT APIs
            string script = @"
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
$asyncOp = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()
$typeName = 'System.WindowsRuntimeSystemExtensions'
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asTask = $asTaskGeneric.MakeGenericMethod([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$netTask = $asTask.Invoke($null, @($asyncOp))
$null = $netTask.Wait(5000)
$sessionManager = $netTask.Result
$session = $sessionManager.GetCurrentSession()
if ($session) {
    $mediaTask = $session.TryGetMediaPropertiesAsync()
    $asTask2 = $asTaskGeneric.MakeGenericMethod([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
    $netTask2 = $asTask2.Invoke($null, @($mediaTask))
    $null = $netTask2.Wait(5000)
    $media = $netTask2.Result
    $playback = $session.GetPlaybackInfo()
    @{
        Title = $media.Title
        Artist = $media.Artist
        AlbumTitle = $media.AlbumTitle
        IsPlaying = ($playback.PlaybackStatus -eq 'Playing')
    } | ConvertTo-Json -Compress
} else {
    Write-Output '{}'
}";

            var result = await Task.Run(() => RunPowerShell(script));

            if (!string.IsNullOrWhiteSpace(result) && result.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;

                if (root.TryGetProperty("Title", out var title) && !string.IsNullOrEmpty(title.GetString()))
                {
                    return new MediaInfo
                    {
                        Title = title.GetString() ?? "",
                        Artist = root.TryGetProperty("Artist", out var artist) ? artist.GetString() ?? "" : "",
                        AlbumTitle = root.TryGetProperty("AlbumTitle", out var album) ? album.GetString() ?? "" : "",
                        IsPlaying = root.TryGetProperty("IsPlaying", out var playing) && playing.GetBoolean()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get media session info");
        }

        return null;
    }

    private static string RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "{}";

        process.StandardInput.Write(script);
        process.StandardInput.Close();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return output.Trim();
    }
}
