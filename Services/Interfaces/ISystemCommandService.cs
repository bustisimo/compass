namespace Compass.Services.Interfaces;

public interface ISystemCommandService
{
    void MediaPlayPause();
    void MediaNextTrack();
    void MediaPrevTrack();
    void VolumeUp();
    void VolumeDown();
    void VolumeMute();
    void MinimizeActiveWindow();
    void MaximizeActiveWindow();
    void RestoreActiveWindow();
    void SnapWindowLeft();
    void SnapWindowRight();
    string ToggleWifi();
    string ToggleBluetooth();
    void OpenDoNotDisturb();
    void LayoutSplit();
    void LayoutStack();
    void LayoutThirds();
}
