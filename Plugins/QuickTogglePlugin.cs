using System.Windows.Media;
using Compass.Services.Interfaces;

namespace Compass.Plugins;

public class QuickTogglePlugin : ICompassPlugin
{
    private readonly ISystemCommandService _systemCommandService;

    private static readonly Dictionary<string, (string Label, string CommandPath, string Icon)> Toggles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wifi"] = ("Toggle WiFi", "COMMAND:TOGGLE_WIFI", "M12,21L15.6,16.2C14.6,15.45 13.35,15 12,15C10.65,15 9.4,15.45 8.4,16.2L12,21M12,3C7.95,3 4.21,4.34 1.2,6.6L3,9C5.5,7.12 8.62,6 12,6C15.38,6 18.5,7.12 21,9L22.8,6.6C19.79,4.34 16.05,3 12,3M12,9C9.3,9 6.81,9.89 4.8,11.4L6.6,13.8C8.1,12.67 9.97,12 12,12C14.03,12 15.9,12.67 17.4,13.8L19.2,11.4C17.19,9.89 14.7,9 12,9Z"),
        ["bluetooth"] = ("Toggle Bluetooth", "COMMAND:TOGGLE_BLUETOOTH", "M14.88,16.29L13,18.17V14.41M13,5.83L14.88,7.71L13,9.58M17.71,7.71L12,2H11V9.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L11,14.41V22H12L17.71,16.29L13.41,12L17.71,7.71Z"),
        ["night light"] = ("Toggle Night Light", "COMMAND:TOGGLE_NIGHTLIGHT", "M12,2A7,7 0 0,0 5,9C5,11.47 6.19,13.66 8,15.07V18H16V15.07C17.81,13.66 19,11.47 19,9A7,7 0 0,0 12,2M9,21V20H15V21A1,1 0 0,1 14,22H10A1,1 0 0,1 9,21Z"),
        ["airplane"] = ("Toggle Airplane Mode", "COMMAND:TOGGLE_AIRPLANE", "M21,16V14L13,9V3.5A1.5,1.5 0 0,0 11.5,2A1.5,1.5 0 0,0 10,3.5V9L2,14V16L10,13.5V19L8,20.5V22L11.5,21L15,22V20.5L13,19V13.5L21,16Z"),
        ["dnd"] = ("Do Not Disturb", "COMMAND:DO_NOT_DISTURB", "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20M7,13H17V11H7"),
        ["do not disturb"] = ("Do Not Disturb", "COMMAND:DO_NOT_DISTURB", "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20M7,13H17V11H7")
    };

    public QuickTogglePlugin(ISystemCommandService systemCommandService)
    {
        _systemCommandService = systemCommandService;
    }

    public string Name => "Quick Toggles";
    public string? SearchPrefix => null;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<AppSearchResult>> SearchAsync(string query)
    {
        var results = new List<AppSearchResult>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var (keyword, (label, commandPath, icon)) in Toggles)
        {
            if (keyword.Contains(lowerQuery) || lowerQuery.Contains(keyword))
            {
                // Avoid duplicates (dnd and do not disturb map to same command)
                if (results.Any(r => r.FilePath == commandPath)) continue;

                results.Add(new AppSearchResult
                {
                    AppName = label,
                    FilePath = commandPath,
                    Subtitle = "Quick toggle — press Enter to switch",
                    GeometryIcon = Geometry.Parse(icon),
                    ResultType = ResultType.QuickToggle
                });
            }
        }

        return Task.FromResult(results);
    }

    public Task<string?> ExecuteAsync(AppSearchResult result)
    {
        if (result.ResultType != ResultType.QuickToggle)
            return Task.FromResult<string?>(null);

        try
        {
            string? output = result.FilePath switch
            {
                "COMMAND:TOGGLE_WIFI" => _systemCommandService.ToggleWifi(),
                "COMMAND:TOGGLE_BLUETOOTH" => _systemCommandService.ToggleBluetooth(),
                "COMMAND:DO_NOT_DISTURB" => OpenDnd(),
                "COMMAND:TOGGLE_NIGHTLIGHT" => ToggleNightLight(),
                "COMMAND:TOGGLE_AIRPLANE" => ToggleAirplaneMode(),
                _ => null
            };
            return Task.FromResult<string?>(output);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    private string OpenDnd()
    {
        _systemCommandService.OpenDoNotDisturb();
        return "Opened Do Not Disturb settings";
    }

    private static string ToggleNightLight()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:nightlight",
                UseShellExecute = true
            });
            return "Opened Night Light settings";
        }
        catch { return "Failed to open Night Light settings"; }
    }

    private static string ToggleAirplaneMode()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:network-airplanemode",
                UseShellExecute = true
            });
            return "Opened Airplane Mode settings";
        }
        catch { return "Failed to open Airplane Mode settings"; }
    }

    public void Dispose() { }
}
