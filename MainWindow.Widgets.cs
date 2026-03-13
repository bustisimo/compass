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

        // Pre-warm system stats cache in background so expanded view opens instantly
        if (_cachedSystemStats == null)
            FireAndForget(PreWarmSystemStatsAsync(), "PreWarmSystemStats");

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
        // Keep timers running if there are floating widgets that need updates
        if (_floatingWidgetWindows.Count == 0)
            StopWidgetTimers();
        AnimateOrSnap(WidgetScale, ScaleTransform.ScaleYProperty, 0, TimeSpan.FromSeconds(0.2),
            new CubicEase { EasingMode = EasingMode.EaseIn },
            () => WidgetScroll.Visibility = Visibility.Collapsed);
    }

    private void RenderWidgets()
    {
        // If an expanded widget view is active, render that instead of the grid
        if (!string.IsNullOrEmpty(_expandedWidgetId))
        {
            var widget = _widgets.FirstOrDefault(w => w.Id == _expandedWidgetId);
            if (widget != null)
            {
                RenderExpandedView(widget);
            }
            else
            {
                _expandedWidgetId = null;
            }
            return;
        }

        // Normal grid rendering
        // Store old positions for animation (safely cast to Border only)
        var oldPositions = new Dictionary<string, Point>();
        foreach (UIElement child in WidgetPanel.Children.Cast<UIElement>().ToList())
        {
            if (child is Border border && border.Tag is string id)
            {
                try
                {
                    var transform = border.TransformToAncestor(WidgetPanel);
                    oldPositions[id] = transform.Transform(new Point(0, 0));
                }
                catch { /* Skip if transform fails */ }
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
                Background = FindResource("CardBrushGlass") as Brush ?? FindResource("CardBrush") as Brush,
                CornerRadius = new CornerRadius(12),
                Width = widgetWidth,
                Margin = widgetMargin,
                Padding = new Thickness(16, 14, 16, 14),
                Tag = widget.Id,
                BorderBrush = FindResource("GlassBorderBrush") as Brush ?? Brushes.Transparent,
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
                if (IsInteractiveElement(e.OriginalSource as DependencyObject, container)) return;
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
                        _dragAdorner = new WidgetDragAdorner(WidgetPanel, container, initialPos, _widgetDragOffset);
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
                container.BorderBrush = FindResource("GlassBorderBrush") as Brush ?? Brushes.Transparent;
                if (_appSettings.AnimationsEnabled)
                {
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.15)));
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.15)));
                }
            };

            // Click to expand widget view (on blank space, not on interactive elements)
            container.MouseLeftButtonUp += (s, e) =>
            {
                if (_isWidgetDragging) return;
                if (IsInteractiveElement(e.OriginalSource as DependencyObject, container)) return;
                _expandedWidgetId = widget.Id;
                RenderWidgets();
                e.Handled = true;
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
                    "Notes" => RenderNotesWidget(),
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
                double displayTemp = weather.Temperature;
                string unit = "C";
                if (_appSettings.TemperatureUnit == "F")
                {
                    displayTemp = (displayTemp * 9 / 5) + 32;
                    unit = "F";
                }
                tempRow.Children.Add(new TextBlock
                {
                    Text = $"{displayTemp:F0}°{unit}",
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

        // Show cached data immediately if available
        var c = _cachedSystemStats;
        double cpuPct = c?.CpuPercent ?? 0;
        double ramPct = c != null && c.RamTotalGB > 0 ? (c.RamUsedGB / c.RamTotalGB * 100) : 0;
        double diskPct = c != null && c.DiskTotalGB > 0 ? (c.DiskUsedGB / c.DiskTotalGB * 100) : 0;

        AddStatsRow(panel, "CPU", cpuPct, $"{cpuPct:F0}%");
        AddStatsRow(panel, "RAM", ramPct, c != null ? $"{c.RamUsedGB:F1}/{c.RamTotalGB:F0} GB" : "0%");
        AddStatsRow(panel, "Disk", diskPct, c != null ? $"{c.DiskUsedGB:F0}/{c.DiskTotalGB:F0} GB" : "0%");

        FireAndForget(UpdateSystemStatsAsync(panel), "UpdateSystemStatsAsync");
        return panel;
    }

    private void AddStatsRow(StackPanel panel, string label, double value, string displayText)
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
            MinWidth = 40
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
        bar.Template = CreateRoundedProgressBarTemplate();
        Grid.SetColumn(bar, 1);

        // Value
        var valueBlock = new TextBlock
        {
            Text = displayText,
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
            // Run all queries in parallel on background threads
            var cpuTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                        if (obj["LoadPercentage"] is ushort load) return (double)load;
                }
                catch { }
                return 0.0;
            });

            var ramTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                    {
                        double totalKB = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        double freeKB = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        return (total: totalKB / 1048576.0, used: (totalKB - freeKB) / 1048576.0);
                    }
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            var diskTask = Task.Run(() =>
            {
                try
                {
                    var drives = System.IO.DriveInfo.GetDrives();
                    var c = drives.FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C")) ?? drives.FirstOrDefault(d => d.IsReady);
                    if (c != null)
                        return (total: c.TotalSize / 1073741824.0, used: (c.TotalSize - c.AvailableFreeSpace) / 1073741824.0);
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            await Task.WhenAll(cpuTask, ramTask, diskTask);

            var stats = new SystemStatsData
            {
                CpuPercent = await cpuTask,
                RamTotalGB = (await ramTask).total,
                RamUsedGB = (await ramTask).used,
                DiskTotalGB = (await diskTask).total,
                DiskUsedGB = (await diskTask).used
            };
            _cachedSystemStats = stats;

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
        // Find the widget content — check main panel first, then floating windows
        UIElement? content = null;

        // Check main widget panel
        foreach (UIElement child in WidgetPanel.Children)
        {
            if (child is Border b && b.Tag as string == widget.Id)
            {
                content = b.Child;
                // Unwrap the Grid wrapper used for pinned widgets
                if (content is Grid grid && grid.Children.Count > 0)
                    content = grid.Children[0];
                break;
            }
        }

        // Check floating widget windows
        UIElement? floatingContent = null;
        if (_floatingWidgetWindows.TryGetValue(widget.Id, out var floatWin) && floatWin.Content is Border floatBorder)
        {
            // Structure: Border > StackPanel(mainPanel) > [titleBar, widgetContent]
            if (floatBorder.Child is StackPanel mainPanel && mainPanel.Children.Count >= 2)
                floatingContent = mainPanel.Children[1]; // second child is the widget content
        }

        // Refresh both if they exist
        if (content != null) RefreshWidgetContent(widget, content);
        if (floatingContent != null) RefreshWidgetContent(widget, floatingContent);
    }

    private void RefreshWidgetContent(CompassWidget widget, UIElement content)
    {
        switch (widget.BuiltInType)
        {
            case "Clock":
                if (content is StackPanel clockPanel && clockPanel.Children.Count >= 2)
                {
                    if (clockPanel.Children[0] is TextBlock timeBlock)
                        timeBlock.Text = DateTime.Now.ToString("h:mm tt");
                    if (clockPanel.Children[1] is TextBlock dateBlock)
                        dateBlock.Text = DateTime.Now.ToString("dddd, MMMM d");
                }
                break;

            case "SystemStats":
                if (content is StackPanel statsPanel)
                    FireAndForget(UpdateSystemStatsAsync(statsPanel), "RefreshSystemStats");
                break;

            case "Weather":
                if (content is StackPanel weatherPanel)
                    FireAndForget(LoadWeatherDataAsync(weatherPanel), "RefreshWeather");
                break;

            case "Calendar":
                if (content is StackPanel calPanel)
                    FireAndForget(LoadCalendarDataAsync(calPanel), "RefreshCalendar");
                break;

            case "Media":
                if (content is StackPanel mediaPanel)
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

    private void SetWeatherLocation_Click(object sender, RoutedEventArgs e)
    {
        // Weather location input moved to expanded widget view
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
            double defaultWidth = widget.WidgetSize == "1x1" ? 220 : 320;
            _appSettings.FloatingWidgets[widgetId] = new FloatingWidgetPosition
            {
                Left = SystemParameters.PrimaryScreenWidth / 2 - defaultWidth / 2,
                Top = SystemParameters.PrimaryScreenHeight / 2 - 100,
                Width = defaultWidth,
                Height = 0 // 0 = auto-size to content
            };
            SaveSettings();
        }

        CreateFloatingWidgetWindow(widget);
        // Ensure timers are running for the floating widget
        if (_widgetTimers.Count == 0)
            StartWidgetTimers();
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
            Width = pos.Width > 0 ? pos.Width : (widget.WidgetSize == "1x1" ? 220 : 320),
            SizeToContent = SizeToContent.Height,
            MinWidth = 160,
            MaxWidth = 500,
            MaxHeight = 600,
            ResizeMode = ResizeMode.NoResize
        };

        // Glass card container
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14)
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 20,
            ShadowDepth = 2,
            Opacity = 0.35,
            Color = Colors.Black,
            Direction = 270
        };

        var mainPanel = new StackPanel();

        // Title bar — drag area with close button
        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Drag the window by the title bar (close button marks its events as Handled to prevent this)
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 1) win.DragMove();
        };

        var titleText = new TextBlock
        {
            Text = widget.Name.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        string capturedId = widget.Id;

        // Close (X) button
        var closeBtn = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            ToolTip = "Dock back to Compass",
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M18,6L6,18 M6,6L18,18"),
                Stroke = FindResource("TextTertiaryBrush") as Brush,
                StrokeThickness = 1.5,
                Stretch = Stretch.Uniform,
                Width = 9,
                Height = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        closeBtn.MouseEnter += (s, e) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x50, 0x50));
        closeBtn.MouseLeave += (s, e) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        closeBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; }; // Prevent DragMove
        closeBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; DockWidget(capturedId); };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

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
                "Notes" => RenderNotesWidget(),
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

        // Save position on move
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
            }
        };
        win.Closed += (s, e) =>
        {
            _floatingWidgetWindows.Remove(capturedId);
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
            // Start timers so floating widgets update live
            if (_floatingWidgetWindows.Count > 0 && _widgetTimers.Count == 0)
                StartWidgetTimers();
        });
    }

    // ---------------------------------------------------------------------------
    // Widget Drag-and-Drop Helpers
    // ---------------------------------------------------------------------------

    private bool IsInteractiveElement(DependencyObject? element, DependencyObject? stopAt = null)
    {
        while (element != null && element != stopAt)
        {
            if (element is ButtonBase || element is Thumb || element is Slider)
                return true;
            // Media control buttons are Border elements with Cursor set to Hand
            if (element is Border border && border.Cursor == System.Windows.Input.Cursors.Hand)
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
            case ResultType.ChatHistory:
                menu.Items.Add(CreateMenuItem("Open", () => LaunchApp(result)));
                menu.Items.Add(CreateMenuItem("Delete", () =>
                {
                    string chatPath = result.FilePath["CHAT:".Length..];
                    try { File.Delete(chatPath); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete saved chat: {File}", chatPath); }
                    ShowSavedChatsAsResults();
                }));
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
        var panel = new StackPanel { Tag = "MediaWidgetPanel" };

        var cached = _cachedMediaInfo;
        bool hasCached = cached != null && (!string.IsNullOrEmpty(cached.Title) || !string.IsNullOrEmpty(cached.Artist));

        // Use Grid so the info text fills remaining width (no blank space on right)
        var contentRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var miniArt = new System.Windows.Controls.Image
        {
            Width = 40,
            Height = 40,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "MediaMiniArt"
        };
        miniArt.Clip = new RectangleGeometry(new Rect(0, 0, 40, 40), 6, 6);
        if (hasCached && _cachedAlbumArt != null)
        {
            miniArt.Source = _cachedAlbumArt;
            miniArt.Visibility = Visibility.Visible;
        }
        else
        {
            miniArt.Visibility = Visibility.Collapsed;
        }
        Grid.SetColumn(miniArt, 0);
        contentRow.Children.Add(miniArt);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(new TextBlock
        {
            Text = hasCached ? cached!.Title : "Nothing playing",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "MediaInfo"
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = hasCached ? (cached!.Artist ?? "") : "",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
            Tag = "MediaInfoArtist"
        });
        Grid.SetColumn(infoStack, 1);
        contentRow.Children.Add(infoStack);
        panel.Children.Add(contentRow);

        // Media controls
        var controlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var prevBtn = CreateMediaButton("M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z", () => _systemCommandService.MediaPrevTrack());
        string initialPlayIcon = (hasCached && cached!.IsPlaying) ? PauseIconData : PlayIconData;
        var playBtn = CreateMediaButton(initialPlayIcon, () => _systemCommandService.MediaPlayPause());
        playBtn.Tag = "MediaMiniPlayBtn";
        var nextBtn = CreateMediaButton("M16,18H18V6H16M6,18L14.5,12L6,6V18Z", () => _systemCommandService.MediaNextTrack());

        controlsPanel.Children.Add(prevBtn);
        controlsPanel.Children.Add(playBtn);
        controlsPanel.Children.Add(nextBtn);
        panel.Children.Add(controlsPanel);

        FireAndForget(UpdateMediaWidget(), "LoadMediaDataAsync");

        _minimizedMediaTimer?.Stop();
        _minimizedMediaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _minimizedMediaTimer.Tick += (s, e) => FireAndForget(UpdateMediaWidget(), "MediaMinimizedRefresh");
        _minimizedMediaTimer.Start();

        return panel;
    }

    private async Task UpdateMediaWidget()
    {
        // Find the current media widget panel in the UI tree
        var mediaPanel = FindMediaWidgetPanel();
        if (mediaPanel == null) return;

        await LoadMediaDataAsync(mediaPanel);
    }

    private StackPanel? FindMediaWidgetPanel()
    {
        // Search in the main widget panel
        foreach (UIElement child in WidgetPanel.Children)
        {
            var result = FindMediaWidgetPanelRecursive(child);
            if (result != null) return result;
        }

        // Also search in floating widget windows
        foreach (var win in _floatingWidgetWindows.Values)
        {
            if (win.Content is UIElement winContent)
            {
                var result = FindMediaWidgetPanelRecursive(winContent);
                if (result != null) return result;
            }
        }

        return null;
    }

    private StackPanel? FindMediaWidgetPanelRecursive(UIElement element)
    {
        if (element is StackPanel sp && sp.Tag as string == "MediaWidgetPanel")
            return sp;

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                var result = FindMediaWidgetPanelRecursive(child);
                if (result != null) return result;
            }
        }

        if (element is Border border && border.Child != null)
        {
            return FindMediaWidgetPanelRecursive(border.Child);
        }

        return null;
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

    private Border CreateExpandedMediaButton(string iconData, Action action)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(iconData),
            Fill = FindResource("TextPrimaryBrush") as Brush,
            Stretch = Stretch.Uniform,
            Width = 18,
            Height = 18
        };
        var btn = new Border
        {
            Child = path,
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(6, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            CornerRadius = new CornerRadius(50)
        };
        btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
        btn.MouseLeftButtonUp += (s, e) => action();
        return btn;
    }

    private void TryOpenMediaSourceApp()
    {
        try
        {
            string appId = _cachedMediaInfo?.SourceAppId ?? "";
            if (string.IsNullOrEmpty(appId)) return;

            // Try launching via shell: protocol (works for UWP/Store apps like Spotify)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"shell:AppsFolder\\{appId}",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback: try as executable name
                string exeName = appId.Contains('.') ? appId.Split('.').Last() : appId;
                if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    exeName += ".exe";
                try { Process.Start(new ProcessStartInfo { FileName = exeName, UseShellExecute = true }); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to launch media source app: {App}", exeName); }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to open media source app"); }
    }

    private string GetFriendlyAppName(string? sourceAppId)
    {
        if (string.IsNullOrEmpty(sourceAppId)) return "";
        // Extract a readable name from the app model ID
        // e.g. "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" → "Spotify"
        // e.g. "chrome.exe" → "Chrome"
        string name = sourceAppId;
        if (name.Contains('!'))
            name = name[(name.LastIndexOf('!') + 1)..];
        else if (name.Contains('.'))
            name = name.Split('.').First();
        // Capitalize first letter
        if (name.Length > 0)
            name = char.ToUpper(name[0]) + name[1..];
        return name;
    }

    private async Task LoadMediaDataAsync(StackPanel panel)
    {
        try
        {
            var media = await _mediaSessionService.GetCurrentMediaAsync();
            _cachedMediaInfo = media;
            UpdateMediaUI(panel, media);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load media data");
            UpdateMediaUI(panel, null);
        }
    }

    private void UpdateMediaUI(StackPanel panel, MediaInfo? media)
    {
        // Collect all tagged elements recursively
        var elements = new Dictionary<string, FrameworkElement>();
        CollectTaggedElements(panel, elements);

        bool hasMedia = media != null && (!string.IsNullOrEmpty(media.Title) || !string.IsNullOrEmpty(media.Artist));

        // --- Cache album art BitmapImage (only reload from disk when song changes) ---
        System.Windows.Media.Imaging.BitmapImage? artBitmap = null;
        if (hasMedia && !string.IsNullOrEmpty(media!.ThumbnailPath) && System.IO.File.Exists(media.ThumbnailPath))
        {
            string artHash = $"{media.Title}_{media.Artist}_{media.AlbumTitle}";
            if (artHash == _cachedAlbumArtHash && _cachedAlbumArt != null)
            {
                artBitmap = _cachedAlbumArt;
            }
            else
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(media.ThumbnailPath);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _cachedAlbumArt = bmp;
                    _cachedAlbumArtHash = artHash;
                    artBitmap = bmp;
                }
                catch { }
            }
        }

        // --- Minimized view elements ---
        if (elements.TryGetValue("MediaInfo", out var infoEl) && infoEl is TextBlock infoText)
            infoText.Text = hasMedia ? (media!.Title ?? "Unknown") : "Nothing playing";

        if (elements.TryGetValue("MediaInfoArtist", out var infoArtistEl) && infoArtistEl is TextBlock infoArtistText)
            infoArtistText.Text = hasMedia ? (media!.Artist ?? "") : "";

        if (elements.TryGetValue("MediaMiniArt", out var miniArtEl) && miniArtEl is System.Windows.Controls.Image miniArt)
        {
            if (artBitmap != null) { miniArt.Source = artBitmap; miniArt.Visibility = Visibility.Visible; }
            else miniArt.Visibility = Visibility.Collapsed;
        }

        // --- Expanded view elements ---
        if (elements.TryGetValue("MediaTitle", out var titleEl) && titleEl is TextBlock titleText)
            titleText.Text = hasMedia ? (media!.Title ?? "Unknown Song") : "Nothing Playing";

        if (elements.TryGetValue("MediaArtist", out var artistEl) && artistEl is TextBlock artistText)
            artistText.Text = hasMedia ? (media!.Artist ?? "") : "";

        if (elements.TryGetValue("MediaAlbumArt", out var artEl) && artEl is System.Windows.Controls.Image albumArt)
        {
            if (artBitmap != null) { albumArt.Source = artBitmap; albumArt.Visibility = Visibility.Visible; }
            else albumArt.Visibility = Visibility.Collapsed;
        }

        if (elements.TryGetValue("MediaStatus", out var statusEl) && statusEl is TextBlock statusText)
            statusText.Text = hasMedia ? (media!.IsPlaying ? "Playing" : "Paused") : "";

        if (elements.TryGetValue("MediaAlbumName", out var albumNameEl) && albumNameEl is TextBlock albumNameText)
            albumNameText.Text = hasMedia && !string.IsNullOrEmpty(media!.AlbumTitle) ? media.AlbumTitle : "";

        // Update source app label
        if (elements.TryGetValue("MediaSourceApp", out var sourceEl) && sourceEl is TextBlock sourceTb)
            sourceTb.Text = hasMedia ? GetFriendlyAppName(media!.SourceAppId) : "";

        // --- Live play/pause icon updates ---
        bool isPlaying = hasMedia && media!.IsPlaying;
        string targetIcon = isPlaying ? PauseIconData : PlayIconData;

        // Minimized play button
        if (elements.TryGetValue("MediaMiniPlayBtn", out var miniPlayEl) && miniPlayEl is Border miniPlayBorder
            && miniPlayBorder.Child is System.Windows.Shapes.Path miniPlayPath)
        {
            miniPlayPath.Data = Geometry.Parse(targetIcon);
        }

        // Expanded play button
        if (elements.TryGetValue("MediaPlayBtn", out var expPlayEl) && expPlayEl is Border expPlayBorder
            && expPlayBorder.Child is System.Windows.Shapes.Path expPlayPath)
        {
            expPlayPath.Data = Geometry.Parse(targetIcon);
        }
    }

    private void CollectTaggedElements(UIElement element, Dictionary<string, FrameworkElement> elements)
    {
        if (element is FrameworkElement fe && fe.Tag is string tag && !string.IsNullOrEmpty(tag))
        {
            elements[tag] = fe;
        }

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                CollectTaggedElements(child, elements);
        }
        else if (element is Border border && border.Child != null)
        {
            CollectTaggedElements(border.Child, elements);
        }
        else if (element is ContentControl cc && cc.Content is UIElement contentEl)
        {
            CollectTaggedElements(contentEl, elements);
        }
    }

    // --- Expanded Widget Views ---

    private void CollapseExpandedWidget()
    {
        try
        {
            _clockExpandedTimer?.Stop();
            _stopwatchTimer?.Stop();
            _timerTimer?.Stop();
            _alarmCheckerTimer?.Stop();
            _mediaRefreshTimer?.Stop();
            _weatherRefreshTimer?.Stop();
            _clockExpandedTimer = null;
            _stopwatchTimer = null;
            _timerTimer = null;
            _alarmCheckerTimer = null;
            _mediaRefreshTimer = null;
            _weatherRefreshTimer = null;

            _clockTabIndex = 0;
            _stopwatch?.Stop();
            _stopwatch = null;
            _timerRemaining = TimeSpan.Zero;

            WidgetScroll.MaxHeight = 300;
            _expandedWidgetId = null;
            RenderWidgets();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error going back from expanded widget");
        }
    }

    private void RenderExpandedView(CompassWidget widget)
    {
        WidgetPanel.Children.Clear();
        WidgetScroll.MaxHeight = 500;

        // Stop minimized media timer when expanding
        _minimizedMediaTimer?.Stop();

        // Compute available width the same way as the normal grid renderer
        double fullWidth = WidgetScroll.ActualWidth;
        if (fullWidth <= 0)
            fullWidth = _appSettings.WindowWidth - 34; // margins: 15+2 each side
        fullWidth -= 4; // WidgetPanel Margin="2" on each side

        // Outer container for everything (header + centered content)
        var outerContainer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Orientation = Orientation.Vertical,
            Width = fullWidth
        };

        // Back button header
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Glass pill back button with arrow icon
        var backRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        backRow.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z"),
            Fill = FindResource("TextSecondaryBrush") as Brush,
            Stretch = Stretch.Uniform,
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0)
        });
        backRow.Children.Add(new TextBlock
        {
            Text = widget.BuiltInType ?? widget.Name ?? widget.Id,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        });
        var backBtn = new Border
        {
            Child = backRow,
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 6, 14, 6),
            Cursor = Cursors.Hand
        };
        backBtn.MouseEnter += (s, e) => backBtn.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
        backBtn.MouseLeave += (s, e) => backBtn.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
        backBtn.MouseLeftButtonUp += (s, e) =>
        {
            CollapseExpandedWidget();
            e.Handled = true;
        };
        header.Children.Add(backBtn);
        outerContainer.Children.Add(header);

        // Widget-specific expanded content
        UIElement content = widget.BuiltInType switch
        {
            "Clock" => RenderClockExpanded(),
            "Weather" => RenderWeatherExpanded(),
            "SystemStats" => RenderSystemStatsExpanded(),
            "Calendar" => RenderCalendarExpanded(),
            "Media" => RenderMediaExpanded(),
            "Notes" => RenderNotesExpanded(),
            _ => new TextBlock { Text = "Unknown widget", Foreground = FindResource("TextTertiaryBrush") as Brush }
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(4, 0, 4, 16)
        };
        outerContainer.Children.Add(scrollViewer);

        WidgetPanel.Children.Add(outerContainer);
    }

    private UIElement RenderClockExpanded()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0)
        };

        // Pill-shaped tab bar
        string[] tabNames = { "Clock", "Stopwatch", "Timer", "Alarms" };
        var tabBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var tabBarInner = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        for (int t = 0; t < tabNames.Length; t++)
        {
            int tabIndex = t;
            bool isActive = _clockTabIndex == tabIndex;

            var tabText = new TextBlock
            {
                Text = tabNames[t],
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = isActive
                    ? Brushes.White
                    : FindResource("TextTertiaryBrush") as Brush
            };
            var tabBtn = new Border
            {
                Child = tabText,
                Background = isActive
                    ? FindResource("AccentBrush") as Brush
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand
            };
            tabBtn.MouseEnter += (s, e) =>
            {
                if (_clockTabIndex != tabIndex)
                    tabBtn.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            };
            tabBtn.MouseLeave += (s, e) =>
            {
                if (_clockTabIndex != tabIndex)
                    tabBtn.Background = Brushes.Transparent;
            };
            tabBtn.MouseLeftButtonUp += (s, e) =>
            {
                _clockTabIndex = tabIndex;
                _expandedWidgetId = "builtin-clock";
                RenderWidgets();
                e.Handled = true;
            };
            tabBarInner.Children.Add(tabBtn);
        }
        tabBar.Child = tabBarInner;
        panel.Children.Add(tabBar);

        // Content for the selected tab
        switch (_clockTabIndex)
        {
            case 0: panel.Children.Add(BuildClockTab()); break;
            case 1: panel.Children.Add(BuildStopwatchTab()); break;
            case 2: panel.Children.Add(BuildTimerTab()); break;
            case 3: panel.Children.Add(BuildAlarmsTab()); break;
        }

        // Start alarm checker if not running
        if (_alarmCheckerTimer == null)
        {
            _alarmCheckerTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _alarmCheckerTimer.Tick += (s, e) => CheckAlarms();
            _alarmCheckerTimer.Start();
        }

        return panel;
    }

    private UIElement BuildClockTab()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var timeText = new TextBlock
        {
            Text = DateTime.Now.ToString("h:mm:ss tt"),
            FontSize = 56,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var dateText = new TextBlock
        {
            Text = DateTime.Now.ToString("dddd, MMMM d, yyyy"),
            FontSize = 13,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        panel.Children.Add(timeText);
        panel.Children.Add(dateText);

        _clockExpandedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockExpandedTimer.Tick += (s, e) =>
        {
            timeText.Text = DateTime.Now.ToString("h:mm:ss tt");
            dateText.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        };
        _clockExpandedTimer.Start();

        return panel;
    }

    private UIElement BuildStopwatchTab()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var swDisplay = new TextBlock
        {
            Text = "00:00.0",
            FontSize = 48,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
            FontFamily = new FontFamily("Consolas, monospace")
        };

        if (_stopwatch?.IsRunning == true || (_stopwatch != null && _stopwatch.Elapsed > TimeSpan.Zero))
        {
            var elapsed = _stopwatch.Elapsed;
            swDisplay.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}";
        }

        panel.Children.Add(swDisplay);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        var swStartStop = CreateModernButton(_stopwatch?.IsRunning == true ? "Stop" : "Start");
        var swReset = CreateModernButton("Reset");

        swStartStop.MouseLeftButtonUp += (s, e) =>
        {
            if (_stopwatch?.IsRunning == true)
            {
                _stopwatch.Stop();
                if (swStartStop.Child is TextBlock tb) tb.Text = "Start";
            }
            else
            {
                _stopwatch ??= new System.Diagnostics.Stopwatch();
                _stopwatch.Start();
                _stopwatchTimer?.Stop();
                _stopwatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _stopwatchTimer.Tick += (s2, e2) =>
                {
                    if (_stopwatch != null)
                    {
                        var el = _stopwatch.Elapsed;
                        swDisplay.Text = $"{el.Minutes:D2}:{el.Seconds:D2}.{el.Milliseconds / 100}";
                    }
                };
                _stopwatchTimer.Start();
                if (swStartStop.Child is TextBlock tb) tb.Text = "Stop";
            }
            e.Handled = true;
        };

        swReset.MouseLeftButtonUp += (s, e) =>
        {
            _stopwatch?.Reset();
            swDisplay.Text = "00:00.0";
            if (swStartStop.Child is TextBlock tb) tb.Text = "Start";
            e.Handled = true;
        };

        if (_stopwatch?.IsRunning == true)
        {
            _stopwatchTimer?.Stop();
            _stopwatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _stopwatchTimer.Tick += (s2, e2) =>
            {
                if (_stopwatch != null)
                {
                    var el = _stopwatch.Elapsed;
                    swDisplay.Text = $"{el.Minutes:D2}:{el.Seconds:D2}.{el.Milliseconds / 100}";
                }
            };
            _stopwatchTimer.Start();
        }

        buttons.Children.Add(swStartStop);
        buttons.Children.Add(swReset);
        panel.Children.Add(buttons);
        return panel;
    }

    private UIElement BuildTimerTab()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var timerDisplay = new TextBlock
        {
            Text = _timerRemaining > TimeSpan.Zero ? FormatTimeSpan(_timerRemaining) : "00:00",
            FontSize = 48,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
            FontFamily = new FontFamily("Consolas, monospace")
        };
        panel.Children.Add(timerDisplay);

        // Preset pill buttons
        var presetsRow = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        int[] presets = { 1, 3, 5, 10, 15, 30 };
        var minutesBox = new TextBox
        {
            Width = 50,
            Text = "5",
            Background = Brushes.Transparent,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 5, 6, 5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12
        };

        var presetButtons = new List<Border>();
        foreach (int mins in presets)
        {
            int capturedMins = mins;
            var pillText = new TextBlock
            {
                Text = $"{mins}m",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = FindResource("TextSecondaryBrush") as Brush
            };
            var pill = new Border
            {
                Child = pillText,
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(3, 3, 3, 3),
                Cursor = Cursors.Hand
            };
            pill.MouseEnter += (s, e) =>
            {
                if (minutesBox.Text != capturedMins.ToString())
                    pill.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            pill.MouseLeave += (s, e) =>
            {
                if (minutesBox.Text != capturedMins.ToString())
                    pill.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
            };
            pill.MouseLeftButtonUp += (s, e) =>
            {
                minutesBox.Text = capturedMins.ToString();
                // Highlight selected preset
                foreach (var pb in presetButtons)
                {
                    if (pb == pill)
                    {
                        pb.Background = FindResource("AccentBrush") as Brush;
                        if (pb.Child is TextBlock ptb) ptb.Foreground = Brushes.White;
                    }
                    else
                    {
                        pb.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
                        if (pb.Child is TextBlock ptb) ptb.Foreground = FindResource("TextSecondaryBrush") as Brush;
                    }
                }
                e.Handled = true;
            };
            presetButtons.Add(pill);
            presetsRow.Children.Add(pill);
        }
        panel.Children.Add(presetsRow);

        // Custom input row (smaller)
        var inputRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        inputRow.Children.Add(minutesBox);
        inputRow.Children.Add(new TextBlock
        {
            Text = "min",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
        panel.Children.Add(inputRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var timerStartBtn = CreateModernButton(_timerTimer?.IsEnabled == true ? "Cancel" : "Start");

        timerStartBtn.MouseLeftButtonUp += (s, e) =>
        {
            if (_timerTimer?.IsEnabled == true)
            {
                _timerTimer.Stop();
                _timerRemaining = TimeSpan.Zero;
                timerDisplay.Text = "00:00";
                if (timerStartBtn.Child is TextBlock tb) tb.Text = "Start";
            }
            else
            {
                if (int.TryParse(minutesBox.Text.Trim(), out int mins) && mins > 0)
                {
                    _timerRemaining = TimeSpan.FromMinutes(mins);
                    _timerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _timerTimer.Stop();
                    _timerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _timerTimer.Tick += (s2, e2) =>
                    {
                        if (_timerRemaining > TimeSpan.Zero)
                        {
                            _timerRemaining = _timerRemaining.Subtract(TimeSpan.FromSeconds(1));
                            timerDisplay.Text = FormatTimeSpan(_timerRemaining);
                            if (_timerRemaining <= TimeSpan.Zero)
                            {
                                _timerTimer.Stop();
                                _notificationService.ShowToast("Timer", "Complete!");
                                if (timerStartBtn.Child is TextBlock tb) tb.Text = "Start";
                            }
                        }
                    };
                    _timerTimer.Start();
                    if (timerStartBtn.Child is TextBlock tb) tb.Text = "Cancel";
                }
            }
            e.Handled = true;
        };

        if (_timerTimer?.IsEnabled == true && _timerRemaining > TimeSpan.Zero)
        {
            timerDisplay.Text = FormatTimeSpan(_timerRemaining);
            _timerTimer.Stop();
            _timerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timerTimer.Tick += (s2, e2) =>
            {
                if (_timerRemaining > TimeSpan.Zero)
                {
                    _timerRemaining = _timerRemaining.Subtract(TimeSpan.FromSeconds(1));
                    timerDisplay.Text = FormatTimeSpan(_timerRemaining);
                    if (_timerRemaining <= TimeSpan.Zero)
                    {
                        _timerTimer.Stop();
                        _notificationService.ShowToast("Timer", "Complete!");
                        if (timerStartBtn.Child is TextBlock tb) tb.Text = "Start";
                    }
                }
            };
            _timerTimer.Start();
        }

        buttons.Children.Add(timerStartBtn);
        panel.Children.Add(buttons);
        return panel;
    }

    private UIElement BuildAlarmsTab()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };
        RenderAlarmsList(panel);
        return panel;
    }

    private void RenderAlarmsList(StackPanel panel)
    {
        panel.Children.Clear();

        if (_appSettings.Alarms.Count == 0)
        {
            var emptyMsg = new TextBlock
            {
                Text = "No alarms set",
                FontSize = 13,
                Foreground = FindResource("TextTertiaryBrush") as Brush,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 32, 0, 32)
            };
            panel.Children.Add(emptyMsg);
        }
        else
        {
            foreach (var alarm in _appSettings.Alarms)
            {
                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Left side: time + label stacked
                var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                leftStack.Children.Add(new TextBlock
                {
                    Text = alarm.Time.ToString("h\\:mm tt"),
                    FontSize = 18,
                    FontWeight = FontWeights.Light,
                    Foreground = FindResource("TextPrimaryBrush") as Brush,
                    Opacity = alarm.IsEnabled ? 1.0 : 0.5
                });
                leftStack.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(alarm.Label) ? "Alarm" : alarm.Label,
                    FontSize = 12,
                    Foreground = FindResource("TextTertiaryBrush") as Brush,
                    Margin = new Thickness(0, 2, 0, 0),
                    Opacity = alarm.IsEnabled ? 1.0 : 0.5
                });
                Grid.SetColumn(leftStack, 0);
                content.Children.Add(leftStack);

                // Right side: toggle + delete icon
                var rightStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var toggle = new CheckBox
                {
                    IsChecked = alarm.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                toggle.Checked += (s, e) => { alarm.IsEnabled = true; SaveSettings(); RenderAlarmsList(panel); };
                toggle.Unchecked += (s, e) => { alarm.IsEnabled = false; SaveSettings(); RenderAlarmsList(panel); };
                rightStack.Children.Add(toggle);

                var deleteIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(150, 255, 100, 100)),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var deleteBtn = new Border
                {
                    Child = deleteIcon,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(4),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                deleteBtn.MouseEnter += (s, e) => deleteIcon.Fill = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                deleteBtn.MouseLeave += (s, e) => deleteIcon.Fill = new SolidColorBrush(Color.FromArgb(150, 255, 100, 100));
                deleteBtn.MouseLeftButtonUp += (s, e) =>
                {
                    _appSettings.Alarms.Remove(alarm);
                    SaveSettings();
                    RenderAlarmsList(panel);
                    e.Handled = true;
                };
                rightStack.Children.Add(deleteBtn);

                Grid.SetColumn(rightStack, 1);
                content.Children.Add(rightStack);

                var card = CreateGlassCard(content, new Thickness(0, 0, 0, 8));
                panel.Children.Add(card);
            }
        }

        var addBtn = CreateModernButton("+ Add Alarm");
        addBtn.Margin = new Thickness(0, 8, 0, 0);
        addBtn.MouseLeftButtonUp += (s, e) =>
        {
            var alarm = new AlarmEntry { Time = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1)) };
            _appSettings.Alarms.Add(alarm);
            SaveSettings();
            RenderAlarmsList(panel);
            e.Handled = true;
        };
        panel.Children.Add(addBtn);
    }

    private void CheckAlarms()
    {
        var now = DateTime.Now.TimeOfDay;
        foreach (var alarm in _appSettings.Alarms.Where(a => a.IsEnabled))
        {
            if (Math.Abs((now - alarm.Time).TotalSeconds) < 60)
            {
                _notificationService.ShowToast("Alarm", alarm.Label);
            }
        }
    }

    private string FormatTimeSpan(TimeSpan ts) => $"{ts.Minutes:D2}:{ts.Seconds:D2}";

    private UIElement RenderWeatherExpanded()
    {
        var centerPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0)
        };

        // Hero temperature
        var tempText = new TextBlock
        {
            Text = "—°",
            FontSize = 56,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Tag = "WeatherTemp"
        };
        centerPanel.Children.Add(tempText);

        // Location name
        var locationText = new TextBlock
        {
            Text = _appSettings.WeatherLocationName,
            FontSize = 14,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            Tag = "WeatherLocation"
        };
        centerPanel.Children.Add(locationText);

        // Condition
        var conditionText = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
            Tag = "WeatherCondition"
        };
        centerPanel.Children.Add(conditionText);

        // 3 glass detail cards in a row
        var detailsRow = new Grid { Margin = new Thickness(0, 0, 0, 16), Tag = "WeatherDetails" };
        detailsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Wind card
        var windStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        windStack.Children.Add(new TextBlock
        {
            Text = "Wind",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        windStack.Children.Add(new TextBlock
        {
            Text = "—",
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "WeatherWind"
        });
        var windCard = CreateGlassCard(windStack, new Thickness(0, 0, 4, 0));
        Grid.SetColumn(windCard, 0);
        detailsRow.Children.Add(windCard);

        // Humidity card
        var humidityStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        humidityStack.Children.Add(new TextBlock
        {
            Text = "Humidity",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        humidityStack.Children.Add(new TextBlock
        {
            Text = "—",
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "WeatherHumidity"
        });
        var humidityCard = CreateGlassCard(humidityStack, new Thickness(4, 0, 4, 0));
        Grid.SetColumn(humidityCard, 1);
        detailsRow.Children.Add(humidityCard);

        // Feels Like card
        var feelsStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        feelsStack.Children.Add(new TextBlock
        {
            Text = "Feels Like",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        feelsStack.Children.Add(new TextBlock
        {
            Text = "—",
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "WeatherFeelsLike"
        });
        var feelsCard = CreateGlassCard(feelsStack, new Thickness(4, 0, 0, 0));
        Grid.SetColumn(feelsCard, 2);
        detailsRow.Children.Add(feelsCard);

        centerPanel.Children.Add(detailsRow);

        // Pill-style °C/°F toggle
        var unitToggle = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        var unitRow = new StackPanel { Orientation = Orientation.Horizontal };

        bool isCelsius = _appSettings.TemperatureUnit == "C";
        var celsiusText = new TextBlock
        {
            Text = "°C",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = isCelsius ? Brushes.White : FindResource("TextTertiaryBrush") as Brush
        };
        var celsiusPill = new Border
        {
            Child = celsiusText,
            Background = isCelsius ? FindResource("AccentBrush") as Brush : Brushes.Transparent,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = Cursors.Hand
        };
        celsiusPill.MouseLeftButtonUp += (s, e) =>
        {
            _appSettings.TemperatureUnit = "C";
            SaveSettings();
            _expandedWidgetId = "builtin-weather";
            RenderWidgets();
            e.Handled = true;
        };

        var fahrenheitText = new TextBlock
        {
            Text = "°F",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = !isCelsius ? Brushes.White : FindResource("TextTertiaryBrush") as Brush
        };
        var fahrenheitPill = new Border
        {
            Child = fahrenheitText,
            Background = !isCelsius ? FindResource("AccentBrush") as Brush : Brushes.Transparent,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = Cursors.Hand
        };
        fahrenheitPill.MouseLeftButtonUp += (s, e) =>
        {
            _appSettings.TemperatureUnit = "F";
            SaveSettings();
            _expandedWidgetId = "builtin-weather";
            RenderWidgets();
            e.Handled = true;
        };

        unitRow.Children.Add(celsiusPill);
        unitRow.Children.Add(fahrenheitPill);
        unitToggle.Child = unitRow;
        centerPanel.Children.Add(unitToggle);

        // Location search section
        var locationSection = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var locationLabel = new TextBlock
        {
            Text = "Change Location",
            FontSize = 12,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        locationSection.Children.Add(locationLabel);

        var searchRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cityBox = new TextBox
        {
            Text = "",
            Background = Brushes.Transparent,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 13,
            CaretBrush = FindResource("TextPrimaryBrush") as Brush
        };
        // Placeholder-like behavior
        var placeholderText = new TextBlock
        {
            Text = "Search city...",
            FontSize = 13,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            IsHitTestVisible = false,
            Margin = new Thickness(12, 9, 0, 0)
        };
        var cityBoxContainer = new Grid();
        cityBoxContainer.Children.Add(cityBox);
        cityBoxContainer.Children.Add(placeholderText);
        cityBox.TextChanged += (s, e) => placeholderText.Visibility =
            string.IsNullOrEmpty(cityBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(cityBoxContainer, 0);
        searchRow.Children.Add(cityBoxContainer);

        var searchBtn = CreateModernButton("Set");
        searchBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(searchBtn, 1);
        searchRow.Children.Add(searchBtn);

        var statusText = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Tag = "WeatherSearchStatus"
        };

        async Task DoWeatherSearch()
        {
            string city = cityBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(city)) return;

            statusText.Text = "Searching...";
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
                    statusText.Text = $"Set to {result.Value.name}";
                    cityBox.Clear();
                    _expandedWidgetId = "builtin-weather";
                    RenderWidgets();
                }
                else
                {
                    statusText.Text = "City not found. Try another name.";
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
        }

        searchBtn.MouseLeftButtonUp += async (s, e) =>
        {
            e.Handled = true;
            await DoWeatherSearch();
        };

        cityBox.PreviewKeyDown += async (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await DoWeatherSearch();
            }
        };

        locationSection.Children.Add(searchRow);
        locationSection.Children.Add(statusText);
        centerPanel.Children.Add(locationSection);

        // Load weather data and update display
        FireAndForget(LoadWeatherExpandedAsync(centerPanel), "LoadWeatherExpanded");

        // Start periodic refresh
        _weatherRefreshTimer?.Stop();
        _weatherRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _weatherRefreshTimer.Tick += (s, e) => FireAndForget(LoadWeatherExpandedAsync(centerPanel), "WeatherLiveRefresh");
        _weatherRefreshTimer.Start();

        return centerPanel;
    }

    private UIElement RenderSystemStatsExpanded()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0)
        };

        // Show cached data immediately if available
        var cs = _cachedSystemStats;
        double cpuPct = cs?.CpuPercent ?? 0;
        double ramPct = cs != null && cs.RamTotalGB > 0 ? (cs.RamUsedGB / cs.RamTotalGB * 100) : 0;
        double diskPct = cs != null && cs.DiskTotalGB > 0 ? (cs.DiskUsedGB / cs.DiskTotalGB * 100) : 0;

        string cpuVal = cs != null ? $"{cs.CpuPercent:F0}%" : "—";
        string ramVal = cs != null ? $"{cs.RamUsedGB:F1} / {cs.RamTotalGB:F0} GB" : "—";
        string diskVal = cs != null ? $"{cs.DiskUsedGB:F0} / {cs.DiskTotalGB:F0} GB" : "—";

        // CPU glass card with progress bar
        panel.Children.Add(CreateStatBar("CPU", cpuVal, cpuPct, "StatsCPU"));

        // Memory glass card with progress bar
        panel.Children.Add(CreateStatBar("Memory", ramVal, ramPct, "StatsRAM"));

        // Storage glass card with progress bar
        panel.Children.Add(CreateStatBar("Storage", diskVal, diskPct, "StatsDisks"));

        // Temperature & Uptime glass card (side by side)
        var infoRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tempContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        tempContent.Children.Add(new TextBlock
        {
            Text = "Temperature",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        tempContent.Children.Add(new TextBlock
        {
            Text = "Checking...",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "StatsTemp"
        });
        var tempCard = CreateGlassCard(tempContent, new Thickness(0, 0, 4, 0));
        Grid.SetColumn(tempCard, 0);
        infoRow.Children.Add(tempCard);

        // Uptime card
        var uptimeTs = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string uptimeStr = uptimeTs.Days > 0
            ? $"{uptimeTs.Days}d {uptimeTs.Hours}h {uptimeTs.Minutes}m"
            : $"{uptimeTs.Hours}h {uptimeTs.Minutes}m";
        var uptimeContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        uptimeContent.Children.Add(new TextBlock
        {
            Text = "Uptime",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        uptimeContent.Children.Add(new TextBlock
        {
            Text = uptimeStr,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "StatsUptime"
        });
        var uptimeCard = CreateGlassCard(uptimeContent, new Thickness(4, 0, 0, 0));
        Grid.SetColumn(uptimeCard, 1);
        infoRow.Children.Add(uptimeCard);

        panel.Children.Add(infoRow);

        FireAndForget(LoadSystemStatsExpandedAsync(panel), "LoadSystemStatsExpanded");

        return panel;
    }

    private async Task PreWarmSystemStatsAsync()
    {
        try
        {
            // Run WMI queries on background threads to warm the COM objects
            var cpuTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                        if (obj["LoadPercentage"] is ushort load) return (double)load;
                }
                catch { }
                return 0.0;
            });

            var ramTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                    {
                        double totalKB = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        double freeKB = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        return (total: totalKB / 1048576.0, used: (totalKB - freeKB) / 1048576.0);
                    }
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            var diskTask = Task.Run(() =>
            {
                try
                {
                    var drives = System.IO.DriveInfo.GetDrives();
                    var c = drives.FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C")) ?? drives.FirstOrDefault(d => d.IsReady);
                    if (c != null)
                        return (total: c.TotalSize / 1073741824.0, used: (c.TotalSize - c.AvailableFreeSpace) / 1073741824.0);
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            await Task.WhenAll(cpuTask, ramTask, diskTask);

            _cachedSystemStats = new SystemStatsData
            {
                CpuPercent = await cpuTask,
                RamTotalGB = (await ramTask).total,
                RamUsedGB = (await ramTask).used,
                DiskTotalGB = (await diskTask).total,
                DiskUsedGB = (await diskTask).used
            };
        }
        catch { }
    }

    private async Task<string?> GetCpuTemperatureAsync()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\CIMV2",
                "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            using var collection = searcher.Get();
            foreach (System.Management.ManagementBaseObject obj in collection)
            {
                var tempK = Convert.ToDouble(obj["Temperature"]);
                var tempC = tempK - 273.15;
                if (tempC > 0 && tempC < 150) return tempC.ToString("F0");
            }
        }
        catch { }

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var collection = searcher.Get();
            foreach (System.Management.ManagementBaseObject obj in collection)
            {
                var tempK = Convert.ToDouble(obj["CurrentTemperature"]) / 10.0;
                var tempC = tempK - 273.15;
                if (tempC > 0 && tempC < 150) return tempC.ToString("F0");
            }
        }
        catch { }

        return null;
    }

    private async Task<string?> GetGpuTemperatureAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=temperature.gpu --format=csv,noheader",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            string output = await p.StandardOutput.ReadToEndAsync();
            if (int.TryParse(output.Trim(), out var temp)) return temp.ToString();
        }
        catch { }
        return null;
    }

    private UIElement RenderCalendarExpanded()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0)
        };

        // Today's date as accent header
        panel.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("dddd, MMMM d"),
            FontSize = 24,
            FontWeight = FontWeights.Light,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var eventsPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        FireAndForget(LoadCalendarExpandedAsync(eventsPanel), "LoadCalendarExpanded");

        panel.Children.Add(eventsPanel);
        return panel;
    }

    private UIElement RenderMediaExpanded()
    {
        var centerPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            Tag = "MediaExpandedContent"
        };

        var cached = _cachedMediaInfo;
        bool hasCached = cached != null && (!string.IsNullOrEmpty(cached.Title) || !string.IsNullOrEmpty(cached.Artist));

        // Album art — 220px with 16px corner radius, click to open source app
        var albumArt = new System.Windows.Controls.Image
        {
            Width = 220,
            Height = 220,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "MediaAlbumArt",
            Cursor = Cursors.Hand
        };
        albumArt.Clip = new RectangleGeometry(new Rect(0, 0, 220, 220), 16, 16);
        if (_cachedAlbumArt != null)
        {
            albumArt.Source = _cachedAlbumArt;
            albumArt.Visibility = Visibility.Visible;
        }
        else
        {
            albumArt.Visibility = Visibility.Collapsed;
        }
        albumArt.MouseLeftButtonUp += (s, e) =>
        {
            TryOpenMediaSourceApp();
            e.Handled = true;
        };
        centerPanel.Children.Add(albumArt);

        // Source app label (e.g. "Spotify") — clickable to open
        string appName = GetFriendlyAppName(cached?.SourceAppId);
        var sourceLabel = new TextBlock
        {
            Text = appName,
            FontSize = 11,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8),
            Cursor = Cursors.Hand,
            Tag = "MediaSourceApp"
        };
        sourceLabel.MouseLeftButtonUp += (s, e) =>
        {
            TryOpenMediaSourceApp();
            e.Handled = true;
        };
        centerPanel.Children.Add(sourceLabel);

        // Song title
        centerPanel.Children.Add(new TextBlock
        {
            Text = hasCached ? cached!.Title : "Nothing Playing",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Tag = "MediaTitle"
        });

        // Artist
        centerPanel.Children.Add(new TextBlock
        {
            Text = hasCached ? (cached!.Artist ?? "") : "",
            FontSize = 14,
            Foreground = FindResource("TextSecondaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Tag = "MediaArtist"
        });

        // Album name
        centerPanel.Children.Add(new TextBlock
        {
            Text = hasCached ? (cached!.AlbumTitle ?? "") : "",
            FontSize = 12,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            Tag = "MediaAlbumName"
        });

        // Controls — 18px icons, frosted glass background
        var controlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        controlsPanel.Children.Add(CreateExpandedMediaButton("M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z", () =>
        {
            _systemCommandService.MediaPrevTrack();
            Task.Delay(300).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
                FireAndForget(LoadMediaDataAsync(centerPanel), "MediaQuickRefresh")));
        }));
        string expInitialIcon = (hasCached && cached!.IsPlaying) ? PauseIconData : PlayIconData;
        // Declare first so the lambda can reference it
        Border? playBtn = null;
        playBtn = CreateExpandedMediaButton(expInitialIcon, () =>
        {
            _systemCommandService.MediaPlayPause();
            // Optimistic UI: immediately toggle the icon
            if (playBtn?.Child is System.Windows.Shapes.Path playPath)
            {
                string currentData = playPath.Data.ToString() ?? "";
                string pauseData = Geometry.Parse(PauseIconData).ToString() ?? "";
                playPath.Data = Geometry.Parse(currentData == pauseData ? PlayIconData : PauseIconData);
            }
        });
        playBtn.Tag = "MediaPlayBtn";
        controlsPanel.Children.Add(playBtn);
        controlsPanel.Children.Add(CreateExpandedMediaButton("M16,18H18V6H16M6,18L14.5,12L6,6V18Z", () =>
        {
            _systemCommandService.MediaNextTrack();
            Task.Delay(300).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
                FireAndForget(LoadMediaDataAsync(centerPanel), "MediaQuickRefresh")));
        }));
        centerPanel.Children.Add(controlsPanel);

        // Volume control row
        var volumeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };

        // Vol down icon
        var volDownIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,9H7L12,4V20L7,15H3V9Z"),
            Fill = FindResource("TextTertiaryBrush") as Brush,
            Stretch = Stretch.Uniform, Width = 12, Height = 12
        };
        var volDownBtn = new Border
        {
            Child = volDownIcon,
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        volDownBtn.MouseEnter += (s, e) => volDownBtn.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
        volDownBtn.MouseLeave += (s, e) => volDownBtn.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));

        // Volume slider — reads/sets system volume via COM
        bool _suppressVolumeEvent = false;
        float currentVol = _systemCommandService.GetMasterVolume();
        var volumeSlider = new Slider
        {
            Minimum = 0, Maximum = 100,
            Value = Math.Round(currentVol * 100),
            Width = 160, VerticalAlignment = VerticalAlignment.Center
        };
        volumeSlider.ValueChanged += (s, e) =>
        {
            if (_suppressVolumeEvent) return;
            _systemCommandService.SetMasterVolume((float)(volumeSlider.Value / 100.0));
        };

        volDownBtn.MouseLeftButtonUp += (s, e) =>
        {
            _systemCommandService.VolumeDown();
            // Sync slider after key event
            _suppressVolumeEvent = true;
            volumeSlider.Value = Math.Max(0, volumeSlider.Value - 2);
            _suppressVolumeEvent = false;
            e.Handled = true;
        };
        volumeRow.Children.Add(volDownBtn);
        volumeRow.Children.Add(volumeSlider);

        // Vol up icon
        var volUpIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,9H7L12,4V20L7,15H3V9M16.5,12C16.5,10.23 15.48,8.71 14,7.97V16C15.48,15.29 16.5,13.77 16.5,12Z"),
            Fill = FindResource("TextTertiaryBrush") as Brush,
            Stretch = Stretch.Uniform, Width = 12, Height = 12
        };
        var volUpBtn = new Border
        {
            Child = volUpIcon,
            Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        volUpBtn.MouseEnter += (s, e) => volUpBtn.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
        volUpBtn.MouseLeave += (s, e) => volUpBtn.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
        volUpBtn.MouseLeftButtonUp += (s, e) =>
        {
            _systemCommandService.VolumeUp();
            _suppressVolumeEvent = true;
            volumeSlider.Value = Math.Min(100, volumeSlider.Value + 2);
            _suppressVolumeEvent = false;
            e.Handled = true;
        };
        volumeRow.Children.Add(volUpBtn);

        centerPanel.Children.Add(volumeRow);

        // Hidden status text for UpdateMediaUI compatibility (not visible)
        centerPanel.Children.Add(new TextBlock
        {
            Tag = "MediaStatus",
            Visibility = Visibility.Collapsed
        });

        // Load and refresh
        FireAndForget(LoadMediaDataAsync(centerPanel), "LoadMediaExpanded");

        _mediaRefreshTimer?.Stop();
        _mediaRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _mediaRefreshTimer.Tick += (s, e) => FireAndForget(LoadMediaDataAsync(centerPanel), "MediaLiveRefresh");
        _mediaRefreshTimer.Start();

        return centerPanel;
    }

    private Border CreatePillButton(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = FindResource("AccentBrush") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Child = textBlock,
            Background = Brushes.Transparent,
            BorderBrush = FindResource("AccentBrush") as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(4)
        };

        border.MouseEnter += (s, e) =>
        {
            border.Background = ((FindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue) switch
            {
                var c => new SolidColorBrush(c) { Opacity = 0.1 }
            };
        };

        border.MouseLeave += (s, e) =>
        {
            border.Background = Brushes.Transparent;
        };

        return border;
    }

    // Helper methods for expanded view async operations

    private async Task LoadWeatherExpandedAsync(StackPanel panel)
    {
        try
        {
            var weather = await _weatherService.GetWeatherAsync(
                _appSettings.WeatherLatitude,
                _appSettings.WeatherLongitude
            );

            if (weather != null)
            {
                double temp = weather.Temperature;
                if (_appSettings.TemperatureUnit == "F")
                    temp = (temp * 9 / 5) + 32;

                // Compute feels-like temperature
                double feelsLike = weather.Temperature;
                if (weather.Temperature <= 10 && weather.WindSpeed > 4.8)
                {
                    double w = weather.WindSpeed;
                    feelsLike = 13.12 + 0.6215 * weather.Temperature
                        - 11.37 * Math.Pow(w, 0.16)
                        + 0.3965 * weather.Temperature * Math.Pow(w, 0.16);
                }
                else if (weather.Temperature >= 27 && weather.Humidity > 40)
                {
                    feelsLike = weather.Temperature + 0.33 * (weather.Humidity / 100.0
                        * 6.105 * Math.Exp(17.27 * weather.Temperature / (237.7 + weather.Temperature))) - 4;
                }
                if (_appSettings.TemperatureUnit == "F")
                    feelsLike = (feelsLike * 9 / 5) + 32;

                string unit = _appSettings.TemperatureUnit == "F" ? "F" : "C";

                // Recursively find and update tagged elements
                var elements = new Dictionary<string, FrameworkElement>();
                CollectTaggedElements(panel, elements);

                if (elements.TryGetValue("WeatherTemp", out var tempEl) && tempEl is TextBlock tempTb)
                    tempTb.Text = $"{temp:F0}°{unit}";
                if (elements.TryGetValue("WeatherLocation", out var locEl) && locEl is TextBlock locTb)
                    locTb.Text = weather.Location ?? _appSettings.WeatherLocationName ?? "Unknown";
                if (elements.TryGetValue("WeatherCondition", out var condEl) && condEl is TextBlock condTb)
                    condTb.Text = weather.Condition ?? "Clear";
                if (elements.TryGetValue("WeatherWind", out var windEl) && windEl is TextBlock windTb)
                    windTb.Text = $"{weather.WindSpeed:F0} km/h";
                if (elements.TryGetValue("WeatherHumidity", out var humEl) && humEl is TextBlock humTb)
                    humTb.Text = $"{weather.Humidity}%";
                if (elements.TryGetValue("WeatherFeelsLike", out var feelsEl) && feelsEl is TextBlock feelsTb)
                    feelsTb.Text = $"{feelsLike:F0}°{unit}";
            }
        }
        catch { }
    }

    private async Task LoadSystemStatsExpandedAsync(StackPanel panel)
    {
        try
        {
            var cpuTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                        if (obj["LoadPercentage"] is ushort load) return (double)load;
                }
                catch { }
                return 0.0;
            });

            var ramTask = Task.Run(() =>
            {
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (System.Management.ManagementBaseObject obj in s.Get())
                    {
                        double totalKB = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        double freeKB = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        return (total: totalKB / 1048576.0, used: (totalKB - freeKB) / 1048576.0);
                    }
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            var diskTask = Task.Run(() =>
            {
                try
                {
                    var drives = System.IO.DriveInfo.GetDrives();
                    var c = drives.FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C")) ?? drives.FirstOrDefault(d => d.IsReady);
                    if (c != null)
                        return (total: c.TotalSize / 1073741824.0, used: (c.TotalSize - c.AvailableFreeSpace) / 1073741824.0);
                }
                catch { }
                return (total: 0.0, used: 0.0);
            });

            var cpuTempTask = GetCpuTemperatureAsync();
            var gpuTempTask = GetGpuTemperatureAsync();

            await Task.WhenAll(cpuTask, ramTask, diskTask, cpuTempTask, gpuTempTask);

            var stats = new SystemStatsData
            {
                CpuPercent = await cpuTask,
                RamTotalGB = (await ramTask).total,
                RamUsedGB = (await ramTask).used,
                DiskTotalGB = (await diskTask).total,
                DiskUsedGB = (await diskTask).used
            };
            _cachedSystemStats = stats;

            var cpuTemp = await cpuTempTask;
            var gpuTemp = await gpuTempTask;

            double ramPct = stats.RamTotalGB > 0 ? (stats.RamUsedGB / stats.RamTotalGB * 100) : 0;
            double diskPct = stats.DiskTotalGB > 0 ? (stats.DiskUsedGB / stats.DiskTotalGB * 100) : 0;

            // Temperature text
            string tempText = "";
            if (!string.IsNullOrEmpty(cpuTemp))
            {
                double temp = double.Parse(cpuTemp);
                if (_appSettings.TemperatureUnit == "F")
                    temp = (temp * 9 / 5) + 32;
                tempText = $"CPU {temp:F0}°";
            }
            if (!string.IsNullOrEmpty(gpuTemp) && double.TryParse(gpuTemp, out var gTemp))
            {
                if (!string.IsNullOrEmpty(tempText)) tempText += "  •  ";
                if (_appSettings.TemperatureUnit == "F")
                    gTemp = (gTemp * 9 / 5) + 32;
                tempText += $"GPU {gTemp:F0}°";
            }
            if (string.IsNullOrEmpty(tempText))
                tempText = "N/A";

            // Recursively update tagged elements inside glass cards
            var elements = new Dictionary<string, FrameworkElement>();
            CollectTaggedElements(panel, elements);

            // Update stat bar values and progress bars
            if (elements.TryGetValue("StatsCPU_Value", out var cpuValEl) && cpuValEl is TextBlock cpuValTb)
                cpuValTb.Text = $"{stats.CpuPercent:F0}%";
            if (elements.TryGetValue("StatsCPU_Bar", out var cpuBarEl) && cpuBarEl is ProgressBar cpuBar)
            {
                cpuBar.Value = stats.CpuPercent;
                cpuBar.Foreground = stats.CpuPercent >= 90
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                    : stats.CpuPercent >= 70
                        ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                        : FindResource("AccentBrush") as Brush;
            }

            if (elements.TryGetValue("StatsRAM_Value", out var ramValEl) && ramValEl is TextBlock ramValTb)
                ramValTb.Text = $"{stats.RamUsedGB:F1} / {stats.RamTotalGB:F0} GB";
            if (elements.TryGetValue("StatsRAM_Bar", out var ramBarEl) && ramBarEl is ProgressBar ramBar)
            {
                ramBar.Value = ramPct;
                ramBar.Foreground = ramPct >= 90
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                    : ramPct >= 70
                        ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                        : FindResource("AccentBrush") as Brush;
            }

            if (elements.TryGetValue("StatsDisks_Value", out var diskValEl) && diskValEl is TextBlock diskValTb)
                diskValTb.Text = $"{stats.DiskUsedGB:F0} / {stats.DiskTotalGB:F0} GB";
            if (elements.TryGetValue("StatsDisks_Bar", out var diskBarEl) && diskBarEl is ProgressBar diskBar)
            {
                diskBar.Value = diskPct;
                diskBar.Foreground = diskPct >= 90
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                    : diskPct >= 70
                        ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                        : FindResource("AccentBrush") as Brush;
            }

            if (elements.TryGetValue("StatsTemp", out var tempEl) && tempEl is TextBlock tempTb)
                tempTb.Text = tempText;
        }
        catch { }
    }

    private async Task LoadCalendarExpandedAsync(StackPanel eventsPanel)
    {
        try
        {
            var events = await _calendarService.GetUpcomingEventsAsync(10);

            if (events.Count == 0)
            {
                eventsPanel.Children.Add(new TextBlock
                {
                    Text = "No upcoming events",
                    FontSize = 13,
                    Foreground = FindResource("TextTertiaryBrush") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                });
                return;
            }

            foreach (var evt in events)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Time badge pill
                var timeBadge = new Border
                {
                    Background = FindResource("AccentBrush") as Brush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                timeBadge.Child = new TextBlock
                {
                    Text = evt.Start.ToString("h:mm tt"),
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = Brushes.White
                };
                row.Children.Add(timeBadge);

                // Event info
                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoStack.Children.Add(new TextBlock
                {
                    Text = evt.Subject,
                    FontSize = 13,
                    Foreground = FindResource("TextPrimaryBrush") as Brush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrEmpty(evt.Location))
                {
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = evt.Location,
                        FontSize = 11,
                        Foreground = FindResource("TextTertiaryBrush") as Brush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                row.Children.Add(infoStack);

                eventsPanel.Children.Add(CreateGlassCard(row, new Thickness(0, 0, 0, 8)));
            }
        }
        catch { }
    }

    private async Task<SystemStatsData> GetSystemStatsAsync()
    {
        var stats = new SystemStatsData();
        try
        {
            // Get CPU usage - quick check, no sleep
            var cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            await Task.Delay(50); // Shorter delay
            stats.CpuPercent = cpuCounter.NextValue();

            // Get RAM usage via WMI for accuracy
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, AvailablePhysicalMemory FROM Win32_OperatingSystem");
                using var collection = searcher.Get();
                foreach (System.Management.ManagementBaseObject obj in collection)
                {
                    long totalMemory = Convert.ToInt64(obj["TotalVisibleMemorySize"]) / 1024; // Convert to MB
                    long availableMemory = Convert.ToInt64(obj["AvailablePhysicalMemory"]) / 1024; // Convert to MB
                    stats.RamTotalGB = totalMemory / 1024.0;
                    stats.RamUsedGB = (totalMemory - availableMemory) / 1024.0;
                    break;
                }
            }
            catch
            {
                // Fallback to performance counter
                var ramCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                long availableRam = (long)ramCounter.NextValue();
                stats.RamTotalGB = 16.0; // Default estimate
                stats.RamUsedGB = stats.RamTotalGB - (availableRam / 1024.0);
            }

            // Get disk usage
            var drives = System.IO.DriveInfo.GetDrives();
            if (drives.Length > 0)
            {
                var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C")) ?? drives[0];
                stats.DiskTotalGB = cDrive.TotalSize / (1024 * 1024 * 1024.0);
                stats.DiskUsedGB = (cDrive.TotalSize - cDrive.AvailableFreeSpace) / (1024 * 1024 * 1024.0);
            }
        }
        catch { }
        return stats;
    }

    // --- Glass card helpers ---

    private Border CreateGlassCard(UIElement child, Thickness? margin = null)
    {
        return new Border
        {
            Child = child,
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = margin ?? new Thickness(0)
        };
    }

    private Border CreateStatBar(string label, string value, double percent, string? tag = null)
    {
        var panel = new StackPanel();

        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (tag != null) valueText.Tag = tag + "_Value";
        DockPanel.SetDock(valueText, Dock.Right);
        headerRow.Children.Add(valueText);
        headerRow.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = FindResource("TextSecondaryBrush") as Brush
        });
        panel.Children.Add(headerRow);

        var bar = new ProgressBar
        {
            Value = Math.Min(100, Math.Max(0, percent)),
            Maximum = 100,
            Height = 6,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            Foreground = percent >= 90
                ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                : percent >= 70
                    ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                    : FindResource("AccentBrush") as Brush,
            BorderThickness = new Thickness(0)
        };
        if (tag != null) bar.Tag = tag + "_Bar";
        bar.Template = CreateRoundedProgressBarTemplate();
        panel.Children.Add(bar);

        var card = CreateGlassCard(panel, new Thickness(0, 0, 0, 8));
        if (tag != null) card.Tag = tag;
        return card;
    }

    // Modern button creation for expanded views
    private Border CreateModernButton(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Child = textBlock,
            Background = FindResource("AccentBrush") as Brush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 10, 16, 10),
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(6)
        };

        border.MouseEnter += (s, e) =>
        {
            if (FindResource("AccentBrush") is SolidColorBrush accentBrush)
            {
                var lighterColor = Color.FromArgb(
                    accentBrush.Color.A,
                    (byte)Math.Min(255, accentBrush.Color.R + 30),
                    (byte)Math.Min(255, accentBrush.Color.G + 30),
                    (byte)Math.Min(255, accentBrush.Color.B + 30)
                );
                border.Background = new SolidColorBrush(lighterColor);
            }
        };

        border.MouseLeave += (s, e) =>
        {
            border.Background = FindResource("AccentBrush") as Brush;
        };

        return border;
    }

    // ---------------------------------------------------------------------------
    // Notes Widget
    // ---------------------------------------------------------------------------

    private DispatcherTimer? _notesSaveTimer;

    private UIElement RenderNotesWidget()
    {
        var panel = new StackPanel();

        // Header
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M14.06,9L15,9.94L5.92,19H5V18.08L14.06,9M17.66,3C17.41,3 17.15,3.1 16.96,3.29L15.13,5.12L18.88,8.87L20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18.17,3.09 17.92,3 17.66,3M14.06,6.19L3,17.25V21H6.75L17.81,9.94L14.06,6.19Z"),
            Fill = FindResource("AccentBrush") as Brush,
            Stretch = Stretch.Uniform,
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 6, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Notes",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as Brush
        });
        panel.Children.Add(header);

        // Preview text
        string notes = _notesService.LoadNotes();
        string preview = string.IsNullOrWhiteSpace(notes)
            ? "Click to add a note..."
            : (notes.Length > 150 ? notes[..150] + "..." : notes);

        var previewText = new TextBlock
        {
            Text = preview,
            FontSize = 12,
            Foreground = string.IsNullOrWhiteSpace(notes)
                ? FindResource("TextTertiaryBrush") as Brush
                : FindResource("TextSecondaryBrush") as Brush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 60
        };
        panel.Children.Add(previewText);

        return panel;
    }

    private UIElement RenderNotesExpanded()
    {
        var panel = new StackPanel();

        // Character count
        string notes = _notesService.LoadNotes();
        var charCount = new TextBlock
        {
            Text = $"{notes.Length} characters",
            FontSize = 11,
            Foreground = FindResource("TextTertiaryBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(charCount);

        // TextBox
        var textBox = new TextBox
        {
            Text = notes,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 200,
            MaxHeight = 350,
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            Foreground = FindResource("TextPrimaryBrush") as Brush,
            Background = FindResource("CardBrushGlass") as Brush ?? FindResource("CardBrush") as Brush,
            BorderBrush = FindResource("GlassBorderBrush") as Brush ?? FindResource("InputBorderBrush") as Brush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            CaretBrush = FindResource("TextPrimaryBrush") as Brush,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // Saved indicator
        var savedIndicator = new TextBlock
        {
            Text = "Saved",
            FontSize = 11,
            Foreground = FindResource("AccentBrush") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0
        };

        // Auto-save with debounce
        textBox.TextChanged += (s, e) =>
        {
            charCount.Text = $"{textBox.Text.Length} characters";

            if (_notesSaveTimer == null)
            {
                _notesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _notesSaveTimer.Tick += (_, _) =>
                {
                    _notesSaveTimer.Stop();
                    _notesService.SaveNotes(textBox.Text);

                    // Flash "Saved" indicator
                    savedIndicator.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.5))
                        {
                            BeginTime = TimeSpan.FromSeconds(0.3)
                        });
                    savedIndicator.Opacity = 1;
                };
            }
            _notesSaveTimer.Stop();
            _notesSaveTimer.Start();
        };

        // Auto-focus
        textBox.Loaded += (s, e) =>
        {
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
        };

        panel.Children.Add(textBox);
        panel.Children.Add(savedIndicator);

        return panel;
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

    public WidgetDragAdorner(UIElement adornedElement, UIElement sourceVisual, Point initialPosition, Point offset) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _position = initialPosition;
        _offset = offset;
        _size = sourceVisual.RenderSize;

        _brush = new VisualBrush(sourceVisual)
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
