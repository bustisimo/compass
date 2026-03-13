using System.Windows.Media;

namespace Compass.Services;

/// <summary>
/// Provides quick actions for the command palette (/ prefix).
/// </summary>
public class QuickActionsService
{
    private readonly List<AppSearchResult> _actions;

    public QuickActionsService()
    {
        _actions = BuildActions();
    }

    public List<AppSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _actions.ToList();

        return _actions
            .Where(a => a.AppName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (a.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private static List<AppSearchResult> BuildActions()
    {
        var actions = new List<AppSearchResult>();

        // System actions
        actions.Add(MakeAction("Toggle WiFi", "System", "COMMAND:TOGGLE_WIFI", ResultType.QuickToggle,
            "M12,21L15.6,16.2C14.6,15.45 13.35,15 12,15C10.65,15 9.4,15.45 8.4,16.2L12,21M12,3C7.95,3 4.21,4.34 1.2,6.6L3,9C5.5,7.12 8.62,6 12,6C15.38,6 18.5,7.12 21,9L22.8,6.6C19.79,4.34 16.05,3 12,3M12,9C9.3,9 6.81,9.89 4.8,11.4L6.6,13.8C8.1,12.67 9.97,12 12,12C14.03,12 15.9,12.67 17.4,13.8L19.2,11.4C17.19,9.89 14.7,9 12,9Z"));
        actions.Add(MakeAction("Toggle Bluetooth", "System", "COMMAND:TOGGLE_BLUETOOTH", ResultType.QuickToggle,
            "M14.88,16.29L13,18.17V14.41M13,5.83L14.88,7.71L13,9.58M17.71,7.71L12,2H11V9.58L6.41,5L5,6.41L10.59,12L5,17.58L6.41,19L11,14.41V22H12L17.71,16.29L13.41,12L17.71,7.71Z"));
        actions.Add(MakeAction("Night Light", "System", "COMMAND:TOGGLE_NIGHTLIGHT", ResultType.QuickToggle,
            "M12,18C11.11,18 10.26,17.8 9.5,17.45C11.56,16.5 13,14.42 13,12C13,9.58 11.56,7.5 9.5,6.55C10.26,6.2 11.11,6 12,6A6,6 0 0,1 18,12A6,6 0 0,1 12,18M20,8.69V4H15.31L12,0.69L8.69,4H4V8.69L0.69,12L4,15.31V20H8.69L12,23.31L15.31,20H20V15.31L23.31,12L20,8.69Z"));
        actions.Add(MakeAction("Airplane Mode", "System", "COMMAND:TOGGLE_AIRPLANE", ResultType.QuickToggle,
            "M20.56,3.91C21.15,4.5 21.15,5.45 20.56,6.03L16.67,9.92L18.79,19.11L17.38,20.53L13.5,13.1L9.6,17L9.96,19.47L8.89,20.53L7.13,17.35L3.94,15.58L5,14.5L7.5,14.87L11.37,11L3.94,7.09L5.36,5.68L14.55,7.8L18.44,3.91C19,3.33 20,3.33 20.56,3.91Z"));
        actions.Add(MakeAction("Do Not Disturb", "System", "COMMAND:DO_NOT_DISTURB", ResultType.QuickToggle,
            "M12,2C6.48,2 2,6.48 2,12C2,17.52 6.48,22 12,22C17.52,22 22,17.52 22,12C22,6.48 17.52,2 12,2M12,20C7.58,20 4,16.42 4,12C4,7.58 7.58,4 12,4C16.42,4 20,7.58 20,12C20,16.42 16.42,20 12,20M7,11H17V13H7V11Z"));
        actions.Add(MakeAction("Volume Mute", "System", "COMMAND:VOLUME_MUTE", ResultType.QuickToggle,
            "M12,4L9.91,6.09L12,8.18M4.27,3L3,4.27L7.73,9H3V15H7L12,20V13.27L16.25,17.53C15.58,18.04 14.83,18.46 14,18.7V20.77C15.38,20.45 16.63,19.82 17.68,18.96L19.73,21L21,19.73L12,10.73M19,12C19,12.94 18.8,13.82 18.46,14.64L19.97,16.15C20.62,14.91 21,13.5 21,12C21,7.72 18,4.14 14,3.23V5.29C16.89,6.15 19,8.83 19,12M16.5,12C16.5,10.23 15.5,8.71 14,7.97V10.18L16.45,12.63C16.5,12.43 16.5,12.21 16.5,12Z"));

        // Layout actions
        actions.Add(MakeAction("Split Layout", "Layout", "COMMAND:LAYOUT_SPLIT", ResultType.WindowLayout,
            "M18,4H6C4.89,4 4,4.89 4,6V18A2,2 0 0,0 6,20H18A2,2 0 0,0 20,18V6C20,4.89 19.1,4 18,4M18,18H13V6H18V18Z"));
        actions.Add(MakeAction("Stack Layout", "Layout", "COMMAND:LAYOUT_STACK", ResultType.WindowLayout,
            "M18,4H6C4.89,4 4,4.89 4,6V18A2,2 0 0,0 6,20H18A2,2 0 0,0 20,18V6C20,4.89 19.1,4 18,4M18,13H6V6H18V13Z"));
        actions.Add(MakeAction("Thirds Layout", "Layout", "COMMAND:LAYOUT_THIRDS", ResultType.WindowLayout,
            "M18,4H6C4.89,4 4,4.89 4,6V18A2,2 0 0,0 6,20H18A2,2 0 0,0 20,18V6C20,4.89 19.1,4 18,4M9.5,18H6V6H9.5V18M14.5,18H9.5V6H14.5V18M18,18H14.5V6H18V18Z"));
        actions.Add(MakeAction("Snap Left", "Layout", "COMMAND:SNAP_LEFT", ResultType.WindowLayout,
            "M4,4H20V20H4V4M6,8V18H12V8H6Z"));
        actions.Add(MakeAction("Snap Right", "Layout", "COMMAND:SNAP_RIGHT", ResultType.WindowLayout,
            "M4,4H20V20H4V4M12,8V18H18V8H12Z"));

        // App actions
        actions.Add(MakeAction("Settings", "App", "COMMAND:SETTINGS", ResultType.Command,
            "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.04 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.04 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z"));
        actions.Add(MakeAction("Shortcuts", "App", "COMMAND:SHORTCUTS", ResultType.Command,
            "M17,3H7A2,2 0 0,0 5,5V21L12,18L19,21V5A2,2 0 0,0 17,3Z"));
        actions.Add(MakeAction("Commands", "App", "COMMAND:COMMANDS", ResultType.Command,
            "M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6Z"));
        actions.Add(MakeAction("Clear Chat", "App", "COMMAND:CLEAR_CHAT", ResultType.Command,
            "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"));
        actions.Add(MakeAction("New Chat", "App", "COMMAND:NEW_CHAT", ResultType.Command,
            "M20,2H4A2,2 0 0,0 2,4V22L6,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2M13,11H11V14H9V11H7V9H9V6H11V9H13V11Z"));

        // Media actions
        actions.Add(MakeAction("Play / Pause", "Media", "COMMAND:MEDIA_PLAY_PAUSE", ResultType.QuickToggle,
            "M8,5.14V19.14L19,12.14L8,5.14Z"));
        actions.Add(MakeAction("Next Track", "Media", "COMMAND:MEDIA_NEXT", ResultType.QuickToggle,
            "M16,18H18V6H16M6,18L14.5,12L6,6V18Z"));
        actions.Add(MakeAction("Previous Track", "Media", "COMMAND:MEDIA_PREV", ResultType.QuickToggle,
            "M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z"));

        return actions;
    }

    private static AppSearchResult MakeAction(string name, string category, string commandPath, ResultType resultType, string geometryData)
    {
        return new AppSearchResult
        {
            AppName = name,
            FilePath = commandPath,
            ResultType = resultType,
            Subtitle = category,
            GeometryIcon = Geometry.Parse(geometryData)
        };
    }
}
