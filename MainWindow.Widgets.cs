using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Compass.Services;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass;

/// <summary>
/// MainWindow - Widget system, rendering, and management
/// </summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------------------
    // Widget system
    // ---------------------------------------------------------------------------

    private void ShowWidgetPanel()
    {
        if (!_appSettings.WidgetsEnabled) return;

        // Always render widgets when showing (in case they changed)
        RenderWidgets();

        if (WidgetScale.ScaleY == 1)
        {
            // Already expanded, just ensure visibility and timers
            WidgetScroll.Visibility = Visibility.Visible;
            StartWidgetTimers();
            return;
        }

        WidgetScroll.Visibility = Visibility.Visible;
        AnimateOrSnap(WidgetScale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromSeconds(0.3),
            new CubicEase { EasingMode = EasingMode.EaseOut });
        StartWidgetTimers();
    }

    private void HideWidgetPanel()
    {
        if (WidgetScale.ScaleY == 0) return;
        StopWidgetTimers();
        AnimateOrSnap(WidgetScale, ScaleTransform.ScaleYProperty, 0, TimeSpan.FromSeconds(0.2),
            new CubicEase { EasingMode = EasingMode.EaseIn },
            () => WidgetScroll.Visibility = Visibility.Collapsed);
    }

    private void RenderWidgets()
    {
        // Store old positions for animation
        var oldPositions = new Dictionary<string, Point>();
        foreach (Border child in WidgetPanel.Children)
        {
            if (child.Tag is string id)
            {
                var transform = child.TransformToAncestor(WidgetPanel);
                oldPositions[id] = transform.Transform(new Point(0, 0));
            }
        }

        WidgetPanel.Children.Clear();
        var orderedWidgets = GetOrderedEnabledWidgets()
            .Where(w => !_appSettings.FloatingWidgets.ContainsKey(w.Id))
            .ToList();

        // Measure actual available width from the ScrollViewer instead of
        // fragile arithmetic over every margin/border in the chain.
        double fullWidth = WidgetScroll.ActualWidth;
        if (fullWidth <= 0)
            fullWidth = _appSettings.WindowWidth - 96; // fallback before first layout
        fullWidth -= 4; // WidgetPanel Margin="2" on each side
        WidgetPanel.Width = fullWidth;
        double gap = 8;
        double halfWidth = (fullWidth - gap) / 2;

        // Pre-compute effective widths: lone 1x1 widgets expand to full width,
        // consecutive 1x1 pairs share the row side-by-side.
        var effectiveWidths = new double[orderedWidgets.Count];
        var effectiveMargins = new Thickness[orderedWidgets.Count];
        for (int j = 0; j < orderedWidgets.Count; j++)
        {
            if (orderedWidgets[j].WidgetSize == "1x1")
            {
                // Check if next widget is also 1x1 → pair them
                if (j + 1 < orderedWidgets.Count && orderedWidgets[j + 1].WidgetSize == "1x1")
                {
                    // First of a pair
                    effectiveWidths[j] = halfWidth;
                    effectiveMargins[j] = new Thickness(0, 0, gap, gap);
                    // Second of the pair
                    j++;
                    effectiveWidths[j] = halfWidth;
                    effectiveMargins[j] = new Thickness(0, 0, 0, gap);
                }
                else
                {
                    // Lone 1x1 → expand to full width
                    effectiveWidths[j] = fullWidth;
                    effectiveMargins[j] = new Thickness(0, 0, 0, gap);
                }
            }
            else
            {
                // 2x1 always gets full width
                effectiveWidths[j] = fullWidth;
                effectiveMargins[j] = new Thickness(0, 0, 0, gap);
            }
        }

        for (int i = 0; i < orderedWidgets.Count; i++)
        {
            var widget = orderedWidgets[i];
            bool isPinned = _appSettings.PinnedWidgetIds.Contains(widget.Id);

            double widgetWidth = effectiveWidths[i];
            Thickness widgetMargin = effectiveMargins[i];

            // Create transform group for both position and scale animations
            var transformGroup = new TransformGroup();
            var translateTransform = new TranslateTransform(0, 0);
            var scaleTransform = new ScaleTransform(1, 1);
            transformGroup.Children.Add(translateTransform);
            transformGroup.Children.Add(scaleTransform);

            var container = new Border
            {
                Background = FindResource("CardBrush") as Brush,
                CornerRadius = new CornerRadius(12),
                Width = widgetWidth,
                Margin = widgetMargin,
                Padding = new Thickness(16, 14, 16, 14),
                Tag = widget.Id,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                AllowDrop = true,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = transformGroup
            };

            // Right-click context menu
            var ctxMenu = new ContextMenu();
            var pinItem = new MenuItem { Header = isPinned ? "Unpin" : "Pin" };
            string capturedId = widget.Id;
            pinItem.Click += (s, e) =>
            {
                if (_appSettings.PinnedWidgetIds.Contains(capturedId))
                    _appSettings.PinnedWidgetIds.Remove(capturedId);
                else
                    _appSettings.PinnedWidgetIds.Add(capturedId);
                SaveSettings();
                RenderWidgets();
            };
            ctxMenu.Items.Add(pinItem);

            var floatItem = new MenuItem { Header = "Float on Desktop" };
            floatItem.Click += (s, e) => FloatWidget(capturedId);
            ctxMenu.Items.Add(floatItem);

            container.ContextMenu = ctxMenu;

            // Drag-and-drop: mouse down (skip if pinned)
            container.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (isPinned) return;
                if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return;
                _widgetDragStartPoint = e.GetPosition(null);
                _widgetDragOffset = e.GetPosition(container); // Store offset within the widget
                _draggedWidgetContainer = container;
            };

            // Drag-and-drop: mouse move (skip if pinned)
            container.PreviewMouseMove += (s, e) =>
            {
                if (isPinned) return;
                if (e.LeftButton != MouseButtonState.Pressed || _draggedWidgetContainer != container) return;
                Point pos = e.GetPosition(null);
                Vector diff = _widgetDragStartPoint - pos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isWidgetDragging = true;

                    // Create adorner to show widget following mouse
                    var adornerLayer = AdornerLayer.GetAdornerLayer(WidgetPanel);
                    if (adornerLayer != null)
                    {
                        // Use mouse position relative to WidgetPanel — matches UpdatePosition coordinate system
                        Point initialPos = e.GetPosition(WidgetPanel);
                        _dragAdorner = new WidgetDragAdorner(container, initialPos, _widgetDragOffset);
                        adornerLayer.Add(_dragAdorner);
                    }

                    container.Opacity = 0.3;
                    var data = new DataObject("WidgetDrag", widget.Id);
                    DragDrop.DoDragDrop(container, data, DragDropEffects.Move);

                    // Clean up adorner
                    if (_dragAdorner != null && adornerLayer != null)
                    {
                        adornerLayer.Remove(_dragAdorner);
                        _dragAdorner = null;
                    }

                    container.Opacity = 1.0;
                    _isWidgetDragging = false;
                    _draggedWidgetContainer = null;
                }
            };

            // Drag-and-drop: drag over
            container.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent("WidgetDrag"))
                {
                    e.Effects = DragDropEffects.Move;
                    // Update adorner position to follow mouse
                    if (_dragAdorner != null)
                    {
                        _dragAdorner.UpdatePosition(e.GetPosition(WidgetPanel));
                    }
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };

            // Drag-and-drop: drag enter (highlight)
            container.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent("WidgetDrag"))
                    container.BorderBrush = FindResource("AccentBrush") as Brush ?? Brushes.CornflowerBlue;
            };

            // Drag-and-drop: drag leave
            container.DragLeave += (s, e) =>
            {
                container.BorderBrush = Brushes.Transparent;
            };

            // Drag-and-drop: drop
            container.Drop += (s, e) =>
            {
                container.BorderBrush = Brushes.Transparent;
                if (e.Data.GetDataPresent("WidgetDrag"))
                {
                    string draggedId = (string)e.Data.GetData("WidgetDrag");
                    string targetId = widget.Id;
                    if (draggedId != targetId)
                    {
                        var order = _appSettings.WidgetOrder;
                        int fromIdx = order.IndexOf(draggedId);
                        int toIdx = order.IndexOf(targetId);
                        if (fromIdx >= 0 && toIdx >= 0)
                        {
                            order.RemoveAt(fromIdx);
                            order.Insert(toIdx, draggedId);
                            SaveSettings();
                            RenderWidgets();
                        }
                    }
                }
                e.Handled = true;
            };

            // Hover effects
            container.MouseEnter += (s, e) =>
            {
                if (_isWidgetDragging) return;
                container.BorderBrush = new SolidColorBrush(
                    ((FindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue))
                { Opacity = 0.3 };
                if (_appSettings.AnimationsEnabled)
                {
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.005, TimeSpan.FromSeconds(0.15)));
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.005, TimeSpan.FromSeconds(0.15)));
                }
            };
            container.MouseLeave += (s, e) =>
            {
                container.BorderBrush = Brushes.Transparent;
                if (_appSettings.AnimationsEnabled)
                {
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.15)));
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.15)));
                }
            };

            UIElement content;
            if (widget.IsBuiltIn)
            {
                content = widget.BuiltInType switch
                {
                    "Clock" => RenderClockWidget(),
                    "Weather" => RenderWeatherWidget(),
                    "SystemStats" => RenderSystemStatsWidget(),
                    "Calendar" => RenderCalendarWidget(),
                    "Media" => RenderMediaWidget(),
                    _ => new TextBlock { Text = "Unknown widget", Foreground = FindResource("TextTertiaryBrush") as Brush }
                };
            }
            else
            {
                content = RenderCustomWidget(widget);
            }

            if (isPinned)
            {
                // Wrap content in a Grid with a pin icon overlay
                var wrapper = new Grid();
                wrapper.Children.Add(content);
                var pinIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12Z"),
                    Fill = FindResource("TextTertiaryBrush") as Brush,
                    Stretch = Stretch.Uniform,
                    Width = 10,
                    Height = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Opacity = 0.6,
                    Margin = new Thickness(0, -4, -6, 0)
                };
                wrapper.Children.Add(pinIcon);
                container.Child = wrapper;
            }
            else
            {
                container.Child = content;
            }
            WidgetPanel.Children.Add(container);
        }

        // Animate widgets from old positions to new positions
        if (_appSettings.AnimationsEnabled && oldPositions.Count > 0)
        {
            WidgetPanel.UpdateLayout(); // Force layout to get new positions

            foreach (Border child in WidgetPanel.Children)
            {
                if (child.Tag is string id && oldPositions.ContainsKey(id))
                {
                    var transform = child.TransformToAncestor(WidgetPanel);
                    var newPos = transform.Transform(new Point(0, 0));
                    var oldPos = oldPositions[id];

                    // Calculate offset from new position to old position
                    double offsetX = oldPos.X - newPos.X;
                    double offsetY = oldPos.Y - newPos.Y;

                    if (Math.Abs(offsetX) > 1 || Math.Abs(offsetY) > 1) // Only animate if moved significantly
                    {
                        if (child.RenderTransform is TransformGroup group && group.Children.Count > 0)
                        {
                            var translateTransform = group.Children[0] as TranslateTransform;
                            if (translateTransform != null)
                            {
                                // Start at old position (offset from new position)
                                translateTransform.X = offsetX;
                                translateTransform.Y = offsetY;

                                // Animate to new position (0, 0 offset)
                                var animX = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3))
                                {
                                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                                };
                                var animY = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3))
                                {
                                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                                };

                                translateTransform.BeginAnimation(TranslateTransform.XProperty, animX);
                                translateTransform.BeginAnimation(TranslateTransform.YProperty, animY);
                            }
                        }
                    }
                }
            }
        }
    }

    private List<CompassWidget> GetOrderedEnabledWidgets()
    {
        var allWidgets = _widgets;
        var enabled = _appSettings.EnabledWidgetIds;
        var order = _appSettings.WidgetOrder;

        return order
            .Where(id => enabled.Contains(id))
            .Select(id => allWidgets.FirstOrDefault(w => w.Id == id))
            .Where(w => w != null)
            .Cast<CompassWidget>()
            .ToList();
    }

    private UIElement RenderClockWidget()
    {
        var panel = new StackPanel();
        var timeText = new TextBlock
        {
            Text = DateTime.Now.ToString("h:mm tt"),
            FontSize = 28,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            Name = "ClockTime"
        };
        var dateText = new TextBlock
        {
            Text = DateTime.Now.ToString("dddd, MMMM d"),
            FontSize = 13,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            Margin = new Thickness(0, 2, 0, 0),
            Name = "ClockDate"
        };
        panel.Children.Add(timeText);
        panel.Children.Add(dateText);
        return panel;
    }

    private UIElement RenderWeatherWidget()
    {
        var panel = new StackPanel();
        var loadingText = new TextBlock
        {
            Text = "Loading weather...",
            FontSize = 13,
            Foreground = FindResource("TextTertiaryBrush") as Brush
        };
        panel.Children.Add(loadingText);

        FireAndForget(LoadWeatherDataAsync(panel), "LoadWeatherDataAsync");
        return panel;
    }

    private async Task LoadWeatherDataAsync(StackPanel panel)
    {
        try
        {
            // Auto-detect location on first run
            if (_appSettings.WeatherLatitude == 0 && _appSettings.WeatherLongitude == 0)
            {
                var location = await _weatherService.GetLocationByIpAsync();
                if (location.HasValue)
                {
                    _appSettings.WeatherLatitude = location.Value.lat;
                    _appSettings.WeatherLongitude = location.Value.lon;
                    _appSettings.WeatherLocationName = location.Value.name;
                    SaveSettings();
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        panel.Children.Clear();
                        panel.Children.Add(new TextBlock
                        {
                            Text = "Set your location in Settings > Widgets",
                            FontSize = 13,
                            Foreground = FindResource("TextTertiaryBrush") as Brush
                        });
                    });
                    return;
                }
            }

            var weather = await _weatherService.GetWeatherAsync(_appSettings.WeatherLatitude, _appSettings.WeatherLongitude);
            await Dispatcher.InvokeAsync(() =>
            {
                panel.Children.Clear();
                if (weather == null)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "Weather unavailable",
                        FontSize = 13,
                        Foreground = FindResource("TextTertiaryBrush") as Brush
                    });
                    return;
                }

                var tempRow = new StackPanel { Orientation = Orientation.Horizontal };
                tempRow.Children.Add(new TextBlock
                {
                    Text = $"{weather.Temperature:F0}°C",
                    FontSize = 24,
                    FontWeight = FontWeights.Light,
                    Foreground = FindResource("TextPrimaryBrush") as Brush
                });
                tempRow.Children.Add(new TextBlock
                {
                    Text = $"  {weather.Condition}",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    Margin = new Thickness(8, 0, 0, 0)
                });
                panel.Children.Add(tempRow);

                var detailText = $"{_appSettings.WeatherLocationName}  •  Wind {weather.WindSpeed} km/h  •  Humidity {weather.Humidity}%";
                panel.Children.Add(new TextBlock
                {
                    Text = detailText,
                    FontSize = 12,
                    Foreground = FindResource("TextTertiaryBrush") as Brush,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load weather widget");
        }
    }

    private UIElement RenderSystemStatsWidget()
    {
        var panel = new StackPanel();
        panel.Tag = "SystemStatsPanel";

        AddStatsRow(panel, "CPU", 0, "Processor");
        AddStatsRow(panel, "RAM", 0, "Memory");
        AddStatsRow(panel, "Disk", 0, "Storage");

        FireAndForget(UpdateSystemStatsAsync(panel), "UpdateSystemStatsAsync");
        return panel;
    }

    private void AddStatsRow(StackPanel panel, string label, double value, string tooltip)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // Label
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            MinWidth = 40,
            ToolTip = tooltip
        };
        Grid.SetColumn(labelBlock, 0);

        // Progress bar
        var bar = new ProgressBar
        {
            Value = value,
            Maximum = 100,
            Height = 8,
            Background = FindResource("SurfaceBrush") as Brush,
            Foreground = FindResource("AccentBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 0)
        };
        // Add rounded corners to progress bar
        bar.Template = CreateRoundedProgressBarTemplate();
        Grid.SetColumn(bar, 1);

        // Value
        var valueBlock = new TextBlock
        {
            Text = $"{value:F0}%",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 80,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(valueBlock, 2);

        row.Children.Add(labelBlock);
        row.Children.Add(bar);
        row.Children.Add(valueBlock);
        panel.Children.Add(row);
    }

    private ControlTemplate CreateRoundedProgressBarTemplate()
    {
        var template = new ControlTemplate(typeof(ProgressBar));

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(FrameworkElement.MinHeightProperty, 8.0);

        // Background border — named PART_Track so WPF sizes PART_Indicator correctly
        var bgBorder = new FrameworkElementFactory(typeof(Border));
        bgBorder.Name = "PART_Track";
        bgBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        bgBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        grid.AppendChild(bgBorder);

        // Foreground border (the actual progress)
        var fgBorder = new FrameworkElementFactory(typeof(Border));
        fgBorder.Name = "PART_Indicator";
        fgBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        fgBorder.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        fgBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        grid.AppendChild(fgBorder);

        template.VisualTree = grid;
        return template;
    }

    private void UpdateStatsRow(Grid? row, double percentage, string displayText)
    {
        if (row == null || row.Children.Count < 3) return;

        // Update progress bar (column 1)
        if (row.Children[1] is ProgressBar bar)
        {
            bar.Value = Math.Min(100, Math.Max(0, percentage));

            // Color-code based on usage
            var brush = percentage switch
            {
                >= 90 => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
                >= 70 => new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Yellow
                _ => FindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(76, 194, 255)) // Accent
            };
            bar.Foreground = brush;
        }

        // Update value text (column 2)
        if (row.Children[2] is TextBlock valueBlock)
        {
            valueBlock.Text = displayText;
        }
    }

    private async Task UpdateSystemStatsAsync(StackPanel panel)
    {
        try
        {
            var stats = await Task.Run(() =>
            {
                var data = new SystemStatsData();

                // CPU
                try
                {
                    using var cpuSearcher = new System.Management.ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                    using var cpuCollection = cpuSearcher.Get();
                    foreach (System.Management.ManagementBaseObject obj in cpuCollection)
                    {
                        if (obj["LoadPercentage"] is ushort load)
                            data.CpuPercent = load;
                    }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "WMI CPU query failed"); }

                // RAM
                try
                {
                    using var osSearcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    using var osCollection = osSearcher.Get();
                    foreach (System.Management.ManagementBaseObject obj in osCollection)
                    {
                        double totalKB = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        double freeKB = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        data.RamTotalGB = totalKB / 1048576.0;
                        data.RamUsedGB = (totalKB - freeKB) / 1048576.0;
                    }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "WMI RAM query failed"); }

                // Disk
                try
                {
                    using var diskSearcher = new System.Management.ManagementObjectSearcher("SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
                    using var diskCollection = diskSearcher.Get();
                    double totalBytes = 0, freeBytes = 0;
                    foreach (System.Management.ManagementBaseObject obj in diskCollection)
                    {
                        totalBytes += Convert.ToDouble(obj["Size"]);
                        freeBytes += Convert.ToDouble(obj["FreeSpace"]);
                    }
                    data.DiskTotalGB = totalBytes / 1073741824.0;
                    data.DiskUsedGB = (totalBytes - freeBytes) / 1073741824.0;
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "WMI Disk query failed"); }

                return data;
            });

            await Dispatcher.InvokeAsync(() =>
            {
                if (panel.Children.Count < 3) return;

                double cpuPct = stats.CpuPercent;
                double ramPct = stats.RamTotalGB > 0 ? (stats.RamUsedGB / stats.RamTotalGB * 100) : 0;
                double diskPct = stats.DiskTotalGB > 0 ? (stats.DiskUsedGB / stats.DiskTotalGB * 100) : 0;

                UpdateStatsRow(panel.Children[0] as Grid, cpuPct, $"{cpuPct:F0}%");
                UpdateStatsRow(panel.Children[1] as Grid, ramPct, $"{stats.RamUsedGB:F1}/{stats.RamTotalGB:F0} GB");
                UpdateStatsRow(panel.Children[2] as Grid, diskPct, $"{stats.DiskUsedGB:F0}/{stats.DiskTotalGB:F0} GB");

                // Feature 11: threshold notifications
                if (_appSettings.NotificationsEnabled)
                {
                    if (cpuPct > _appSettings.CpuAlertThreshold && (DateTime.Now - _lastCpuAlert).TotalMinutes > 5)
                    {
                        _lastCpuAlert = DateTime.Now;
                        _notificationService.ShowToast("High CPU Usage", $"CPU is at {cpuPct:F0}%");
                    }
                    if (diskPct > _appSettings.DiskAlertThreshold && (DateTime.Now - _lastDiskAlert).TotalMinutes > 5)
                    {
                        _lastDiskAlert = DateTime.Now;
                        _notificationService.ShowToast("Disk Space Low", $"Disk is {diskPct:F0}% full");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update system stats widget");
        }
    }

    private UIElement RenderCustomWidget(CompassWidget widget)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(widget.XamlContent))
                return new TextBlock { Text = widget.Name, Foreground = FindResource("TextTertiaryBrush") as Brush };

            var element = System.Windows.Markup.XamlReader.Parse(widget.XamlContent) as UIElement;
            return element ?? new TextBlock { Text = "Failed to render widget", Foreground = FindResource("TextTertiaryBrush") as Brush };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render custom widget {WidgetId}", widget.Id);
            return new TextBlock
            {
                Text = $"Widget error: {ex.Message}",
                FontSize = 12,
                Foreground = Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    // --- Widget timers ---

    private void StartWidgetTimers()
    {
        StopWidgetTimers();
        var enabledWidgets = GetOrderedEnabledWidgets();

        foreach (var widget in enabledWidgets)
        {
            if (!widget.IsBuiltIn) continue;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(widget.RefreshIntervalSeconds)
            };
            var capturedWidget = widget;
            timer.Tick += (s, e) => RefreshBuiltInWidget(capturedWidget);
            timer.Start();
            _widgetTimers[widget.Id] = timer;
        }
    }

    private void StopWidgetTimers()
    {
        foreach (var timer in _widgetTimers.Values)
            timer.Stop();
        _widgetTimers.Clear();
    }

    private void RefreshBuiltInWidget(CompassWidget widget)
    {
        // Find the container for this widget
        Border? container = null;
        foreach (UIElement child in WidgetPanel.Children)
        {
            if (child is Border b && b.Tag as string == widget.Id)
            {
                container = b;
                break;
            }
        }
        if (container == null) return;

        switch (widget.BuiltInType)
        {
            case "Clock":
                if (container.Child is StackPanel clockPanel && clockPanel.Children.Count >= 2)
                {
                    if (clockPanel.Children[0] is TextBlock timeBlock)
                        timeBlock.Text = DateTime.Now.ToString("h:mm tt");
                    if (clockPanel.Children[1] is TextBlock dateBlock)
                        dateBlock.Text = DateTime.Now.ToString("dddd, MMMM d");
                }
                break;

            case "SystemStats":
                if (container.Child is StackPanel statsPanel)
                    FireAndForget(UpdateSystemStatsAsync(statsPanel), "RefreshSystemStats");
                break;

            case "Weather":
                if (container.Child is StackPanel weatherPanel)
                    FireAndForget(LoadWeatherDataAsync(weatherPanel), "RefreshWeather");
                break;

            case "Calendar":
                if (container.Child is StackPanel calPanel)
                    FireAndForget(LoadCalendarDataAsync(calPanel), "RefreshCalendar");
                break;

            case "Media":
                if (container.Child is StackPanel mediaPanel)
                    FireAndForget(LoadMediaDataAsync(mediaPanel), "RefreshMedia");
                break;
        }
    }

    // --- Widget XAML validation ---

    private bool ValidateWidgetXaml(string xaml, out string error)
    {
        error = "";

        string[] forbidden = { "x:Class", "clr-namespace", "Click=", "MouseDown=", "MouseUp=",
            "Binding ", "Binding}", "StaticResource", "DynamicResource", "CommandBinding",
            "EventSetter", "EventTrigger" };

        foreach (var pattern in forbidden)
        {
            if (xaml.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                error = $"XAML contains forbidden pattern: {pattern}";
                return false;
            }
        }

        try
        {
            var element = System.Windows.Markup.XamlReader.Parse(xaml);
            if (element == null)
            {
                error = "XAML parsed to null";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"XAML parse error: {ex.Message}";
            return false;
        }
    }

    // --- Widget manager handlers ---

    private void SyncWidgetControls()
    {
        WidgetsEnabledCheck.IsChecked = _appSettings.WidgetsEnabled;
        WeatherLocationBox.Text = _appSettings.WeatherLocationName;

        _widgets = _widgetService.GetAllWidgets();

        // Apply saved widget sizes
        foreach (var widget in _widgets)
        {
            if (_appSettings.WidgetSizes.TryGetValue(widget.Id, out var size))
            {
                widget.WidgetSize = size;
            }
        }

        var orderedWidgets = new List<CompassWidget>();
        foreach (var id in _appSettings.WidgetOrder)
        {
            var w = _widgets.FirstOrDefault(x => x.Id == id);
            if (w != null) orderedWidgets.Add(w);
        }
        // Add any widgets not in the order list
        foreach (var w in _widgets)
        {
            if (!orderedWidgets.Any(x => x.Id == w.Id))
                orderedWidgets.Add(w);
        }

        // Build display items with correct enabled state for data binding
        var displayItems = orderedWidgets.Select(w =>
            new WidgetDisplayItem(w, _appSettings.EnabledWidgetIds.Contains(w.Id))
        ).ToList();

        WidgetsList.ItemsSource = displayItems;
    }

    private void WidgetsEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        _appSettings.WidgetsEnabled = WidgetsEnabledCheck.IsChecked == true;
        SaveSettings();
    }

    private void WidgetToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        var displayItem = cb.DataContext as WidgetDisplayItem;
        string? widgetId = displayItem?.Id ?? cb.Tag as string;
        if (string.IsNullOrEmpty(widgetId)) return;

        if (cb.IsChecked == true)
        {
            if (!_appSettings.EnabledWidgetIds.Contains(widgetId))
                _appSettings.EnabledWidgetIds.Add(widgetId);
        }
        else
        {
            _appSettings.EnabledWidgetIds.Remove(widgetId);
        }
        SaveSettings();
        RenderWidgets();
    }

    private void MoveWidgetUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var displayItem = btn.DataContext as WidgetDisplayItem;
        var widgetId = displayItem?.Id;
        if (string.IsNullOrEmpty(widgetId)) return;

        int idx = _appSettings.WidgetOrder.IndexOf(widgetId);
        if (idx > 0)
        {
            _appSettings.WidgetOrder.RemoveAt(idx);
            _appSettings.WidgetOrder.Insert(idx - 1, widgetId);
            SaveSettings();
            SyncWidgetControls();
        }
    }

    private void MoveWidgetDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var displayItem = btn.DataContext as WidgetDisplayItem;
        var widgetId = displayItem?.Id;
        if (string.IsNullOrEmpty(widgetId)) return;

        int idx = _appSettings.WidgetOrder.IndexOf(widgetId);
        if (idx >= 0 && idx < _appSettings.WidgetOrder.Count - 1)
        {
            _appSettings.WidgetOrder.RemoveAt(idx);
            _appSettings.WidgetOrder.Insert(idx + 1, widgetId);
            SaveSettings();
            SyncWidgetControls();
        }
    }

    private void DeleteWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var displayItem = btn.DataContext as WidgetDisplayItem;
        string? widgetId = displayItem?.Id ?? btn.Tag as string;
        if (string.IsNullOrEmpty(widgetId)) return;

        var widget = _widgets.FirstOrDefault(w => w.Id == widgetId);
        if (widget == null || widget.IsBuiltIn) return;

        _widgetService.DeleteWidget(widget.Id);
        _appSettings.EnabledWidgetIds.Remove(widget.Id);
        _appSettings.WidgetOrder.Remove(widget.Id);
        SaveSettings();
        _widgets = _widgetService.GetAllWidgets();
        SyncWidgetControls();
        RenderWidgets();
    }

    private async void CreateWidget_Click(object sender, RoutedEventArgs e)
    {
        string name = NewWidgetName.Text.Trim();
        string description = NewWidgetDescription.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            await ShowModernDialog("Missing Info", "Please provide both a name and description.");
            return;
        }

        try
        {
            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "Generating..."; }

            string xaml = await _geminiService.GenerateWidgetXamlAsync(description, _appSettings);

            if (!ValidateWidgetXaml(xaml, out string error))
            {
                await ShowModernDialog("Widget Error", $"Generated XAML failed validation:\n{error}");
                return;
            }

            var widget = new CompassWidget
            {
                Name = name,
                Description = description,
                XamlContent = xaml,
                IsBuiltIn = false,
                RefreshIntervalSeconds = 0
            };

            _widgetService.SaveWidget(widget);
            _appSettings.EnabledWidgetIds.Add(widget.Id);
            _appSettings.WidgetOrder.Add(widget.Id);
            SaveSettings();
            _widgets = _widgetService.GetAllWidgets();
            SyncWidgetControls();

            NewWidgetName.Clear();
            NewWidgetDescription.Clear();
            await ShowModernDialog("Success", $"Widget '{name}' created successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create widget");
            await ShowModernDialog("Error", $"Error creating widget: {ex.Message}");
        }
        finally
        {
            if (sender is Button b) { b.IsEnabled = true; b.Content = "Generate Widget"; }
        }
    }

    private async void SetWeatherLocation_Click(object sender, RoutedEventArgs e)
    {
        string city = WeatherLocationBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(city))
        {
            await ShowModernDialog("Missing Info", "Please enter a city name.");
            return;
        }

        try
        {
            var result = await _weatherService.GeocodeAsync(city);
            if (result.HasValue)
            {
                _appSettings.WeatherLatitude = result.Value.lat;
                _appSettings.WeatherLongitude = result.Value.lon;
                _appSettings.WeatherLocationName = result.Value.name;
                _weatherService.ClearCache();
                SaveSettings();
                WeatherLocationBox.Text = result.Value.name;
                await ShowModernDialog("Success", $"Weather location set to {result.Value.name}.");
            }
            else
            {
                await ShowModernDialog("Not Found", "Could not find that location. Try a different city name.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set weather location");
            await ShowModernDialog("Error", $"Error: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // Welcome Screen
    // ---------------------------------------------------------------------------

    private void WelcomeGetStarted_Click(object sender, RoutedEventArgs e)
    {
        WelcomeOverlay.Visibility = Visibility.Collapsed;

        // Restore main content visibility
        SpotlightView.Visibility = Visibility.Visible;
        ManagerView.Visibility = Visibility.Collapsed;

        // Restore MainBorder styling
        ApplyPersonalizationSettings();

        if (WelcomeDontShowAgain.IsChecked == true)
        {
            _appSettings.HasSeenWelcome = true;
            SaveSettings();
        }

        // Hide the window after welcome
        this.Hide();
    }

    // ---------------------------------------------------------------------------
    // Modern Dialog Helper
    // ---------------------------------------------------------------------------

    private Task<bool> ShowModernDialog(string title, string message,
        string primaryText = "OK", string? secondaryText = null)
    {
        _dialogTcs = new TaskCompletionSource<bool>();

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogButtons.Children.Clear();

        if (secondaryText != null)
        {
            var secondaryBtn = new Button
            {
                Content = secondaryText,
                Style = FindResource("PillButtonStyle") as Style,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 80
            };
            secondaryBtn.Click += (s, e) =>
            {
                HideDialog();
                _dialogTcs?.TrySetResult(false);
            };
            DialogButtons.Children.Add(secondaryBtn);
        }

        var primaryBtn = new Button
        {
            Content = primaryText,
            Style = FindResource("SettingsButtonStyle") as Style,
            MinWidth = 80
        };
        primaryBtn.Click += (s, e) =>
        {
            HideDialog();
            _dialogTcs?.TrySetResult(true);
        };
        DialogButtons.Children.Add(primaryBtn);

        DialogOverlay.Visibility = Visibility.Visible;
        if (_appSettings.AnimationsEnabled)
        {
            DialogOverlay.Opacity = 0;
            DialogOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
            DialogCard.RenderTransform = new ScaleTransform(0.95, 0.95);
            DialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
            ((ScaleTransform)DialogCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.95, 1, TimeSpan.FromSeconds(0.15))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            ((ScaleTransform)DialogCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.95, 1, TimeSpan.FromSeconds(0.15))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Handle Escape key
        DialogOverlay.PreviewKeyDown += DialogOverlay_PreviewKeyDown;
        DialogOverlay.Focus();

        return _dialogTcs.Task;
    }

    private void DialogOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideDialog();
            _dialogTcs?.TrySetResult(false);
            e.Handled = true;
        }
    }

    private void HideDialog()
    {
        DialogOverlay.PreviewKeyDown -= DialogOverlay_PreviewKeyDown;
        DialogOverlay.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------------------
    // Empty State Helpers
    // ---------------------------------------------------------------------------

    private void UpdateEmptyStates()
    {
        ShortcutsEmptyState.Visibility = _userShortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CommandsEmptyState.Visibility = _extensions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------------------
    // Widget Float on Desktop
    // ---------------------------------------------------------------------------

    private void FloatWidget(string widgetId)
    {
        var widget = _widgets.FirstOrDefault(w => w.Id == widgetId);
        if (widget == null) return;

        // Save floating position (use center of screen as default)
        if (!_appSettings.FloatingWidgets.ContainsKey(widgetId))
        {
            _appSettings.FloatingWidgets[widgetId] = new FloatingWidgetPosition
            {
                Left = SystemParameters.PrimaryScreenWidth / 2 - 150,
                Top = SystemParameters.PrimaryScreenHeight / 2 - 100,
                Width = widget.WidgetSize == "1x1" ? 250 : 340,
                Height = 200
            };
            SaveSettings();
        }

        CreateFloatingWidgetWindow(widget);
        RenderWidgets();
    }

    private void CreateFloatingWidgetWindow(CompassWidget widget)
    {
        if (_floatingWidgetWindows.ContainsKey(widget.Id)) return;
        var pos = _appSettings.FloatingWidgets[widget.Id];

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Left = pos.Left,
            Top = pos.Top,
            Width = pos.Width,
            Height = pos.Height,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };

        var border = new Border
        {
            Background = FindResource("CardBrush") as Brush,
            CornerRadius = new CornerRadius(12),
            BorderBrush = FindResource("InputBorderBrush") as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12)
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 15,
            ShadowDepth = 3,
            Opacity = 0.3,
            Color = Colors.Black
        };

        var mainPanel = new DockPanel();

        // Title bar
        var titleBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) win.DragMove(); };
        var titleText = new TextBlock
        {
            Text = widget.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dockBtn = new Button
        {
            Content = "Dock",
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Background = Brushes.Transparent,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        string capturedId = widget.Id;
        dockBtn.Click += (s, e) => DockWidget(capturedId);
        DockPanel.SetDock(dockBtn, Dock.Right);
        titleBar.Children.Add(dockBtn);
        titleBar.Children.Add(titleText);

        DockPanel.SetDock(titleBar, Dock.Top);
        mainPanel.Children.Add(titleBar);

        // Widget content
        UIElement content;
        if (widget.IsBuiltIn)
        {
            content = widget.BuiltInType switch
            {
                "Clock" => RenderClockWidget(),
                "Weather" => RenderWeatherWidget(),
                "SystemStats" => RenderSystemStatsWidget(),
                "Calendar" => RenderCalendarWidget(),
                "Media" => RenderMediaWidget(),
                _ => new TextBlock { Text = widget.Name, Foreground = FindResource("TextTertiaryBrush") as Brush }
            };
        }
        else
        {
            content = RenderCustomWidget(widget);
        }
        mainPanel.Children.Add(content);

        border.Child = mainPanel;
        win.Content = border;

        // Save position on move/resize
        win.LocationChanged += (s, e) =>
        {
            if (_appSettings.FloatingWidgets.TryGetValue(capturedId, out var p))
            {
                p.Left = win.Left;
                p.Top = win.Top;
            }
        };
        win.SizeChanged += (s, e) =>
        {
            if (_appSettings.FloatingWidgets.TryGetValue(capturedId, out var p))
            {
                p.Width = win.ActualWidth;
                p.Height = win.ActualHeight;
            }
        };
        win.Closed += (s, e) =>
        {
            _floatingWidgetWindows.Remove(capturedId);
            // Save position on close
            SaveSettings();
        };

        _floatingWidgetWindows[widget.Id] = win;
        win.Show();
    }

    private void DockWidget(string widgetId)
    {
        if (_floatingWidgetWindows.TryGetValue(widgetId, out var win))
        {
            win.Close();
            _floatingWidgetWindows.Remove(widgetId);
        }
        _appSettings.FloatingWidgets.Remove(widgetId);
        if (!_appSettings.WidgetOrder.Contains(widgetId))
            _appSettings.WidgetOrder.Add(widgetId);
        SaveSettings();
        RenderWidgets();
    }

    private void RestoreFloatingWidgets()
    {
        if (_appSettings.FloatingWidgets.Count == 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            foreach (var kvp in _appSettings.FloatingWidgets.ToList())
            {
                var widget = _widgets.FirstOrDefault(w => w.Id == kvp.Key);
                if (widget != null)
                    CreateFloatingWidgetWindow(widget);
            }
        });
    }

    // ---------------------------------------------------------------------------
    // Widget Drag-and-Drop Helpers
    // ---------------------------------------------------------------------------

    private bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is ButtonBase || element is Thumb || element is Slider)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    // ---------------------------------------------------------------------------
    // Feature 9: Action shortcuts / context menu on results
    // ---------------------------------------------------------------------------

    private void ShowResultContextMenu(AppSearchResult result)
    {
        var menu = new ContextMenu();

        switch (result.ResultType)
        {
            case ResultType.Application:
                menu.Items.Add(CreateMenuItem("Open", () => LaunchApp(result)));
                menu.Items.Add(CreateMenuItem("Run as Admin", () => RunAsAdmin(result)));
                menu.Items.Add(CreateMenuItem("Open File Location", () => OpenFileLocation(result)));
                menu.Items.Add(CreateMenuItem("Copy Path", () => { System.Windows.Clipboard.SetText(result.FilePath); this.Hide(); }));
                break;
            case ResultType.Extension:
                menu.Items.Add(CreateMenuItem("Run", () => LaunchApp(result)));
                menu.Items.Add(CreateMenuItem("Delete", () =>
                {
                    string trigger = result.FilePath.Replace("EXTENSION:", "");
                    var ext = _extensions.FirstOrDefault(e => e.TriggerName == trigger);
                    if (ext != null) { _extService.DeleteExtension(ext.TriggerName); FireAndForget(RefreshExtensionCacheAsync(), "RefreshExtensionCache"); }
                }));
                break;
            case ResultType.FileContent:
            case ResultType.RecentFile:
            case ResultType.File:
                menu.Items.Add(CreateMenuItem("Open", () => LaunchApp(result)));
                menu.Items.Add(CreateMenuItem("Open Containing Folder", () => OpenFileLocation(result)));
                menu.Items.Add(CreateMenuItem("Copy Path", () => { System.Windows.Clipboard.SetText(result.FilePath); this.Hide(); }));
                break;
            case ResultType.Bookmark:
                menu.Items.Add(CreateMenuItem("Open", () => LaunchApp(result)));
                string url = result.FilePath.StartsWith("BOOKMARK:") ? result.FilePath["BOOKMARK:".Length..] : result.FilePath;
                menu.Items.Add(CreateMenuItem("Copy URL", () => { System.Windows.Clipboard.SetText(url); this.Hide(); }));
                break;
            case ResultType.Calculator:
                menu.Items.Add(CreateMenuItem("Copy Result", () => { System.Windows.Clipboard.SetText(result.AppName); this.Hide(); }));
                break;
            case ResultType.Shortcut:
                menu.Items.Add(CreateMenuItem("Use Shortcut", () => LaunchApp(result)));
                break;
            default:
                menu.Items.Add(CreateMenuItem("Open", () => LaunchApp(result)));
                break;
        }

        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => action();
        return item;
    }

    private void RunAsAdmin(AppSearchResult result)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = result.FilePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            InputBox.Clear();
            this.Hide();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Run as admin failed"); }
    }

    private void OpenFileLocation(AppSearchResult result)
    {
        try
        {
            string path = result.FilePath;
            if (path.StartsWith("BOOKMARK:") || path.StartsWith("EXTENSION:") || path.StartsWith("COMMAND:"))
                return;
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            InputBox.Clear();
            this.Hide();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Open file location failed"); }
    }

    // ---------------------------------------------------------------------------
    // Feature 10: Clipboard manager panel
    // ---------------------------------------------------------------------------

    private void ShowClipboardPanel()
    {
        if (!this.IsVisible)
        {
            this.Show();
            this.Activate();
        }

        HideWidgetPanel();
        var entries = _clipboardHistoryService.GetAll();
        var results = entries.Select(entry => new AppSearchResult
        {
            AppName = entry.Text.Length > 60 ? entry.Text[..60] + "..." : entry.Text,
            FilePath = $"CLIPBOARD:{entry.Timestamp.Ticks}",
            Subtitle = entry.Timestamp.ToString("g"),
            GeometryIcon = Geometry.Parse("M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M7,7H17V5H19V19H5V5H7V7Z"),
            ResultType = ResultType.ClipboardHistory
        }).ToList();

        SearchResultList.ItemsSource = results;
        ShowSearchList();
        if (results.Any())
            SearchResultList.SelectedIndex = 0;

        InputBox.Focus();
    }


    // ---------------------------------------------------------------------------
    // Feature 11: Toast notifications
    // ---------------------------------------------------------------------------

    private void ShowToast(string title, string message)
    {
        if (!_appSettings.NotificationsEnabled) return;

        ToastTitle.Text = title;
        ToastMessage.Text = message;
        ToastContainer.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
        ToastContainer.BeginAnimation(OpacityProperty, fadeIn);

        _toastDismissTimer?.Stop();
        if (_toastDismissTimer == null)
        {
            _toastDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastDismissTimer.Tick += (s, e) =>
            {
                _toastDismissTimer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
                fadeOut.Completed += (s2, e2) => ToastContainer.Visibility = Visibility.Collapsed;
                ToastContainer.BeginAnimation(OpacityProperty, fadeOut);
            };
        }
        _toastDismissTimer.Start();
    }

    // ---------------------------------------------------------------------------
    // Feature 14: Calendar widget rendering
    // ---------------------------------------------------------------------------

    private UIElement RenderCalendarWidget()
    {
        var panel = new StackPanel();
        var titleText = new TextBlock
        {
            Text = "Upcoming Events",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(titleText);

        var loadingText = new TextBlock
        {
            Text = "Loading calendar...",
            FontSize = 12,
            Foreground = FindResource("TextTertiaryBrush") as Brush
        };
        panel.Children.Add(loadingText);

        FireAndForget(LoadCalendarDataAsync(panel), "LoadCalendarDataAsync");
        return panel;
    }

    private async Task LoadCalendarDataAsync(StackPanel panel)
    {
        try
        {
            var events = await _calendarService.GetUpcomingEventsAsync(5);
            panel.Children.Clear();

            var titleText = new TextBlock
            {
                Text = "Upcoming Events",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 6)
            };
            panel.Children.Add(titleText);

            if (events.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No upcoming events",
                    FontSize = 12,
                    Foreground = FindResource("TextTertiaryBrush") as Brush
                });
                return;
            }

            foreach (var ev in events.Take(5))
            {
                var eventPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                eventPanel.Children.Add(new TextBlock
                {
                    Text = ev.Start.ToString("MMM d, h:mm tt"),
                    FontSize = 11,
                    Foreground = FindResource("AccentBrush") as Brush
                });
                eventPanel.Children.Add(new TextBlock
                {
                    Text = ev.Subject,
                    FontSize = 13,
                    Foreground = FindResource("TextPrimaryBrush") as Brush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrEmpty(ev.Location))
                {
                    eventPanel.Children.Add(new TextBlock
                    {
                        Text = ev.Location,
                        FontSize = 11,
                        Foreground = FindResource("TextTertiaryBrush") as Brush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                panel.Children.Add(eventPanel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load calendar data");
            panel.Children.Clear();
            panel.Children.Add(new TextBlock
            {
                Text = "Calendar unavailable",
                FontSize = 12,
                Foreground = FindResource("TextTertiaryBrush") as Brush
            });
        }
    }

    // ---------------------------------------------------------------------------
    // Feature 15: Media widget rendering
    // ---------------------------------------------------------------------------

    private UIElement RenderMediaWidget()
    {
        var panel = new StackPanel();
        var titleText = new TextBlock
        {
            Text = "Now Playing",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(titleText);

        var infoText = new TextBlock
        {
            Text = "Loading...",
            FontSize = 12,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            Name = "MediaInfo"
        };
        panel.Children.Add(infoText);

        // Media controls row
        var controlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var prevBtn = CreateMediaButton("M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z", () => _systemCommandService.MediaPrevTrack());
        var playBtn = CreateMediaButton("M8,5.14V19.14L19,12.14L8,5.14Z", () => _systemCommandService.MediaPlayPause());
        var nextBtn = CreateMediaButton("M16,18H18V6H16M6,18L14.5,12L6,6V18Z", () => _systemCommandService.MediaNextTrack());

        controlsPanel.Children.Add(prevBtn);
        controlsPanel.Children.Add(playBtn);
        controlsPanel.Children.Add(nextBtn);
        panel.Children.Add(controlsPanel);

        FireAndForget(LoadMediaDataAsync(panel), "LoadMediaDataAsync");
        return panel;
    }

    private Border CreateMediaButton(string iconData, Action action)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(iconData),
            Fill = FindResource("AccentBrush") as Brush,
            Stretch = Stretch.Uniform,
            Width = 16,
            Height = 16
        };
        var btn = new Border
        {
            Child = path,
            Background = Brushes.Transparent,
            Padding = new Thickness(8),
            Margin = new Thickness(4, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            CornerRadius = new CornerRadius(4)
        };
        btn.MouseLeftButtonUp += (s, e) => action();
        return btn;
    }

    private async Task LoadMediaDataAsync(StackPanel panel)
    {
        try
        {
            var media = await _mediaSessionService.GetCurrentMediaAsync();
            // Find the info text block
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb && tb.Name == "MediaInfo")
                {
                    if (media != null && !string.IsNullOrEmpty(media.Title))
                    {
                        string status = media.IsPlaying ? "\u25B6" : "\u23F8";
                        tb.Text = $"{status} {media.Artist} \u2014 {media.Title}";
                    }
                    else
                    {
                        tb.Text = "Nothing playing";
                    }
                    break;
                }
            }
        }
        catch
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb && tb.Name == "MediaInfo")
                {
                    tb.Text = "Media unavailable";
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Adorner that shows a visual representation of the widget being dragged.
/// </summary>
public class WidgetDragAdorner : Adorner
{
    private readonly VisualBrush _brush;
    private Point _position;
    private readonly Point _offset;
    private readonly Size _size;

    public WidgetDragAdorner(UIElement adornedElement, Point initialPosition, Point offset) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _position = initialPosition;
        _offset = offset;
        _size = adornedElement.RenderSize;

        _brush = new VisualBrush(adornedElement)
        {
            Opacity = 0.8,
            Stretch = Stretch.None
        };
    }

    public void UpdatePosition(Point newPosition)
    {
        _position = newPosition;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var rect = new Rect(
            _position.X - _offset.X,
            _position.Y - _offset.Y,
            _size.Width,
            _size.Height
        );

        drawingContext.PushOpacity(0.3);
        drawingContext.DrawRoundedRectangle(
            Brushes.Black,
            null,
            new Rect(rect.X + 4, rect.Y + 4, rect.Width, rect.Height),
            12, 12
        );
        drawingContext.Pop();

        drawingContext.DrawRoundedRectangle(_brush, null, rect, 12, 12);
    }
}
