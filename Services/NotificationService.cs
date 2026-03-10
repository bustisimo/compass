namespace Compass.Services;

public class NotificationService
{
    public event Action<string, string>? OnToast;

    public void ShowToast(string title, string message)
    {
        OnToast?.Invoke(title, message);
    }
}
