namespace Compass;

public class CompassWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string XamlContent { get; set; } = "";
    public bool IsBuiltIn { get; set; } = false;
    public string BuiltInType { get; set; } = "";
    public int RefreshIntervalSeconds { get; set; } = 60;
    public string WidgetSize { get; set; } = "2x1";
}

public class FloatingWidgetPosition
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
}

/// <summary>
/// Wrapper for displaying widgets in the settings list with proper data binding.
/// </summary>
public class WidgetDisplayItem : System.ComponentModel.INotifyPropertyChanged
{
    public CompassWidget Widget { get; }
    public string Id => Widget.Id;
    public string Name => Widget.Name;
    public string Description => Widget.Description;

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public System.Windows.Visibility DeleteVisibility =>
        Widget.IsBuiltIn ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public WidgetDisplayItem(CompassWidget widget, bool isEnabled)
    {
        Widget = widget;
        _isEnabled = isEnabled;
    }
}
