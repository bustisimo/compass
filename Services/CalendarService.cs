using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class CalendarService
{
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(ILogger<CalendarService> logger)
    {
        _logger = logger;
    }

    public async Task<List<CalendarEvent>> GetUpcomingEventsAsync(int count = 5)
    {
        var events = new List<CalendarEvent>();

        try
        {
            string script = @"
try {
    $outlook = New-Object -ComObject Outlook.Application
    $calendar = $outlook.Session.GetDefaultFolder(9)
    $items = $calendar.Items
    $items.Sort('[Start]')
    $items.IncludeRecurrences = $true
    $now = Get-Date -Format 'M/d/yyyy h:mm tt'
    $filtered = $items.Restrict(""[Start] >= '$now'"")
    $results = @()
    $count = 0
    foreach ($item in $filtered) {
        if ($count -ge " + count + @") { break }
        $results += @{
            Subject = $item.Subject
            Start = $item.Start.ToString('o')
            End = $item.End.ToString('o')
            Location = if ($item.Location) { $item.Location } else { '' }
        }
        $count++
    }
    $results | ConvertTo-Json -Compress
} catch {
    Write-Output '[]'
}";

            var result = await Task.Run(() => RunPowerShell(script));

            if (!string.IsNullOrWhiteSpace(result) && result.TrimStart().StartsWith('['))
            {
                var parsed = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(result);
                if (parsed != null)
                {
                    foreach (var item in parsed)
                    {
                        events.Add(new CalendarEvent
                        {
                            Subject = item.GetValueOrDefault("Subject", ""),
                            Start = DateTime.TryParse(item.GetValueOrDefault("Start", ""), out var start) ? start : DateTime.Now,
                            End = DateTime.TryParse(item.GetValueOrDefault("End", ""), out var end) ? end : DateTime.Now,
                            Location = item.GetValueOrDefault("Location", "")
                        });
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(result) && result.TrimStart().StartsWith('{'))
            {
                // Single event returned as object instead of array
                var item = JsonSerializer.Deserialize<Dictionary<string, string>>(result);
                if (item != null)
                {
                    events.Add(new CalendarEvent
                    {
                        Subject = item.GetValueOrDefault("Subject", ""),
                        Start = DateTime.TryParse(item.GetValueOrDefault("Start", ""), out var start) ? start : DateTime.Now,
                        End = DateTime.TryParse(item.GetValueOrDefault("End", ""), out var end) ? end : DateTime.Now,
                        Location = item.GetValueOrDefault("Location", "")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get calendar events");
        }

        return events;
    }

    private static string RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "[]";

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return output.Trim();
    }
}
