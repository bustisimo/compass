using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Compass;

/// <summary>
/// MainWindow - Utility helper methods
/// </summary>
public partial class MainWindow
{
    // Scroll bar hover handlers (smooth fade)
    private void ScrollBar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Grid grid)
        {
            var thumb = FindVisualChild<Thumb>(grid);
            if (thumb != null)
                AnimateOrSnap(thumb, UIElement.OpacityProperty, 0.8, TimeSpan.FromSeconds(0.15));
        }
    }

    private void ScrollBar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Grid grid)
        {
            var thumb = FindVisualChild<Thumb>(grid);
            if (thumb != null)
                AnimateOrSnap(thumb, UIElement.OpacityProperty, 0, TimeSpan.FromSeconds(0.3));
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

}
