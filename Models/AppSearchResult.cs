using System.Windows.Media;

namespace Compass;

public class AppSearchResult
{
    public string AppName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public ImageSource? AppIcon { get; set; }
    public Geometry? GeometryIcon { get; set; }
}
