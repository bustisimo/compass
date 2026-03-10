using System.Diagnostics;
using System.Runtime.InteropServices;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class SystemCommandService : ISystemCommandService
{
    // --- P/Invoke for media/volume keys ---
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // --- P/Invoke for window management ---
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_MUTE = 0xAD;

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly ILogger<SystemCommandService> _logger;

    public SystemCommandService(ILogger<SystemCommandService> logger)
    {
        _logger = logger;
    }

    private static void SendKey(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // --- Media controls ---

    public void MediaPlayPause() => SendKey(VK_MEDIA_PLAY_PAUSE);
    public void MediaNextTrack() => SendKey(VK_MEDIA_NEXT_TRACK);
    public void MediaPrevTrack() => SendKey(VK_MEDIA_PREV_TRACK);
    public void VolumeUp() => SendKey(VK_VOLUME_UP);
    public void VolumeDown() => SendKey(VK_VOLUME_DOWN);
    public void VolumeMute() => SendKey(VK_VOLUME_MUTE);

    // --- Window management ---

    public void MinimizeActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_MINIMIZE);
    }

    public void MaximizeActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_MAXIMIZE);
    }

    public void RestoreActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_RESTORE);
    }

    public void SnapWindowLeft()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        // Restore first so we can reposition
        ShowWindow(hwnd, SW_RESTORE);

        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var wa = screen.WorkingArea;
        SetWindowPos(hwnd, IntPtr.Zero, wa.Left, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void SnapWindowRight()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        ShowWindow(hwnd, SW_RESTORE);

        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var wa = screen.WorkingArea;
        SetWindowPos(hwnd, IntPtr.Zero, wa.Left + wa.Width / 2, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // --- Window layout commands ---

    private List<IntPtr> GetVisibleWindows(int max = 3)
    {
        var windows = new List<IntPtr>();
        var currentProcess = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindowTextLength(hWnd) == 0) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == currentProcess) return true;

            // Skip minimized windows
            GetWindowRect(hWnd, out RECT rect);
            if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0) return true;

            windows.Add(hWnd);
            return windows.Count < max + 2; // get a few extra
        }, IntPtr.Zero);

        return windows.Take(max).ToList();
    }

    public void LayoutSplit()
    {
        var windows = GetVisibleWindows(2);
        if (windows.Count < 2) return;

        var screen = System.Windows.Forms.Screen.FromHandle(windows[0]);
        var wa = screen.WorkingArea;

        ShowWindow(windows[0], SW_RESTORE);
        SetWindowPos(windows[0], IntPtr.Zero, wa.Left, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER);

        ShowWindow(windows[1], SW_RESTORE);
        SetWindowPos(windows[1], IntPtr.Zero, wa.Left + wa.Width / 2, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER);
    }

    public void LayoutStack()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_MAXIMIZE);
    }

    public void LayoutThirds()
    {
        var windows = GetVisibleWindows(3);
        if (windows.Count < 3) return;

        var screen = System.Windows.Forms.Screen.FromHandle(windows[0]);
        var wa = screen.WorkingArea;
        int third = wa.Width / 3;

        for (int i = 0; i < 3; i++)
        {
            ShowWindow(windows[i], SW_RESTORE);
            SetWindowPos(windows[i], IntPtr.Zero, wa.Left + third * i, wa.Top, third, wa.Height, SWP_NOZORDER);
        }
    }

    // --- Quick settings via PowerShell ---

    public string ToggleWifi()
    {
        return RunPowerShell(
            "$adapter = Get-NetAdapter | Where-Object { $_.Name -like '*Wi-Fi*' -or $_.Name -like '*Wireless*' } | Select-Object -First 1; " +
            "if ($adapter.Status -eq 'Up') { Disable-NetAdapter -Name $adapter.Name -Confirm:$false; 'WiFi disabled' } " +
            "else { Enable-NetAdapter -Name $adapter.Name -Confirm:$false; 'WiFi enabled' }");
    }

    public string ToggleBluetooth()
    {
        return RunPowerShell(
            "$bt = Get-PnpDevice -Class Bluetooth | Where-Object { $_.FriendlyName -notlike '*Radio*' } | Select-Object -First 1; " +
            "if ($bt.Status -eq 'OK') { Disable-PnpDevice -InstanceId $bt.InstanceId -Confirm:$false; 'Bluetooth disabled' } " +
            "else { Enable-PnpDevice -InstanceId $bt.InstanceId -Confirm:$false; 'Bluetooth enabled' }");
    }

    public void OpenDoNotDisturb()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:quiethours",
            UseShellExecute = true
        });
    }

    private string RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "Failed to start PowerShell";

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);

        return string.IsNullOrWhiteSpace(error) ? output.Trim() : $"Error: {error.Trim()}";
    }
}
