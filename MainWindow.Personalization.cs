using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass;

/// <summary>
/// MainWindow - Personalization and theming
/// </summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------------------
    // Personalization
    // ---------------------------------------------------------------------------

    private void ApplyPersonalizationSettings()
    {
        try
        {
            // Stop any existing background animation
            StopBackgroundAnimation();

            // Apply background based on type
            ApplyBackground();

            // Border
            MainBorder.BorderThickness = new Thickness(_appSettings.BorderThickness);
            if (TryParseColor(_appSettings.BorderColor, out var borderColor))
                MainBorder.BorderBrush = new SolidColorBrush(borderColor);

            // Corner radius
            MainBorder.CornerRadius = new CornerRadius(_appSettings.BorderRadius);

            if (_appSettings.WindowWidth > 0) this.Width = _appSettings.WindowWidth;
            this.SizeToContent = SizeToContent.Height;

            this.FontFamily = new FontFamily(_appSettings.FontFamily);
            this.FontSize = _appSettings.FontSize;
            PlaceholderText.Text = _appSettings.CompassBoxDefaultText;
            MainBorder.Opacity = _appSettings.WindowOpacity;

            // Dynamic accent color
            if (TryParseColor(_appSettings.AccentColor, out var accentColor))
            {
                Resources["AccentBrush"] = new SolidColorBrush(accentColor);
                var hoverColor = Color.FromArgb(accentColor.A,
                    (byte)(accentColor.R * 0.85),
                    (byte)(accentColor.G * 0.85),
                    (byte)(accentColor.B * 0.85));
                Resources["AccentBrushHover"] = new SolidColorBrush(hoverColor);
            }

            // Determine if this is a light or dark theme based on primary color luminance
            bool isLightTheme = false;
            if (TryParseColor(_appSettings.PrimaryColor, out var pc))
            {
                double luminance = (0.299 * pc.R + 0.587 * pc.G + 0.114 * pc.B) / 255.0;
                isLightTheme = luminance > 0.5;
            }

            ApplyThemeBrushes(isLightTheme);
            ApplyCompactMode();

            UpdateCurrentPersonalizationDisplay();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply personalization settings");
        }
    }

    private void ApplyBackground()
    {
        if (TryParseColor(_appSettings.PrimaryColor, out var primaryColor))
        {
            byte opacityByte = 255; // Fully opaque background

            switch (_appSettings.BackgroundType)
            {
                case "LinearGradient":
                    {
                        var brush = CreateGradientBrush(primaryColor, opacityByte);
                        if (brush != null)
                        {
                            // Convert angle to start/end points
                            double radians = _appSettings.GradientAngle * Math.PI / 180.0;
                            double cos = Math.Cos(radians);
                            double sin = Math.Sin(radians);
                            var lgb = new LinearGradientBrush();
                            lgb.StartPoint = new Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
                            lgb.EndPoint = new Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
                            foreach (var stop in brush) lgb.GradientStops.Add(stop);
                            MainBorder.Background = lgb;
                        }
                        else
                        {
                            MainBorder.Background = new SolidColorBrush(Color.FromArgb(opacityByte, primaryColor.R, primaryColor.G, primaryColor.B));
                        }
                    }
                    break;

                case "RadialGradient":
                    {
                        var stops = CreateGradientBrush(primaryColor, opacityByte);
                        if (stops != null)
                        {
                            var rgb = new RadialGradientBrush();
                            rgb.Center = new Point(0.5, 0.5);
                            rgb.GradientOrigin = new Point(0.5, 0.5);
                            rgb.RadiusX = 0.7;
                            rgb.RadiusY = 0.7;
                            foreach (var stop in stops) rgb.GradientStops.Add(stop);
                            MainBorder.Background = rgb;
                        }
                        else
                        {
                            MainBorder.Background = new SolidColorBrush(Color.FromArgb(opacityByte, primaryColor.R, primaryColor.G, primaryColor.B));
                        }
                    }
                    break;

                default: // "Solid"
                    MainBorder.Background = new SolidColorBrush(Color.FromArgb(opacityByte, primaryColor.R, primaryColor.G, primaryColor.B));
                    break;
            }
        }
    }

    private List<GradientStop>? CreateGradientBrush(Color fallbackColor, byte opacity)
    {
        if (!TryParseColor(_appSettings.GradientStartColor, out var startColor) ||
            !TryParseColor(_appSettings.GradientEndColor, out var endColor))
            return null;

        startColor = Color.FromArgb(opacity, startColor.R, startColor.G, startColor.B);
        endColor = Color.FromArgb(opacity, endColor.R, endColor.G, endColor.B);

        var stops = new List<GradientStop>
        {
            new(startColor, 0.0),
            new(endColor, 1.0)
        };
        return stops;
    }


    private void StopBackgroundAnimation()
    {
        MainBorder.Clip = null;
        // Restore default window shadow
        MainBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 30,
            ShadowDepth = 2,
            Opacity = 0.4,
            Color = Colors.Black,
            Direction = 270
        };
    }

    private void ApplyThemeBrushes(bool isLight)
    {
        if (isLight)
        {
            Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            Resources["TextPlaceholderBrush"] = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            Resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
            Resources["CardBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            Resources["HoverBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
            Resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
            Resources["IconBrush"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }
        else
        {
            Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            Resources["TextPlaceholderBrush"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            Resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15));
            Resources["CardBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            Resources["HoverBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            Resources["InputBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            Resources["IconBrush"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }

        // Custom overrides
        if (TryParseColor(_appSettings.SecondaryColor, out var secColor))
        {
            Resources["SurfaceBrush"] = new SolidColorBrush(secColor);
            // Derive card/hover from secondary
            Resources["CardBrush"] = new SolidColorBrush(isLight
                ? Color.FromArgb(255, (byte)Math.Max(0, secColor.R - 10), (byte)Math.Max(0, secColor.G - 10), (byte)Math.Max(0, secColor.B - 10))
                : Color.FromArgb(255, (byte)Math.Min(255, secColor.R + 16), (byte)Math.Min(255, secColor.G + 16), (byte)Math.Min(255, secColor.B + 16)));
            Resources["HoverBrush"] = new SolidColorBrush(isLight
                ? Color.FromArgb(255, (byte)Math.Max(0, secColor.R - 18), (byte)Math.Max(0, secColor.G - 18), (byte)Math.Max(0, secColor.B - 18))
                : Color.FromArgb(255, (byte)Math.Min(255, secColor.R + 22), (byte)Math.Min(255, secColor.G + 22), (byte)Math.Min(255, secColor.B + 22)));
        }

        if (TryParseColor(_appSettings.TextColor, out var txtColor))
        {
            Resources["TextPrimaryBrush"] = new SolidColorBrush(txtColor);
            // Derive secondary/tertiary from the custom text color
            byte r = txtColor.R, g = txtColor.G, b = txtColor.B;
            double txtLum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            if (txtLum > 0.5)
            {
                Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, (byte)(r * 0.8), (byte)(g * 0.8), (byte)(b * 0.8)));
                Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, (byte)(r * 0.55), (byte)(g * 0.55), (byte)(b * 0.55)));
            }
            else
            {
                Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, (byte)Math.Min(255, r + 60), (byte)Math.Min(255, g + 60), (byte)Math.Min(255, b + 60)));
                Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, (byte)Math.Min(255, r + 100), (byte)Math.Min(255, g + 100), (byte)Math.Min(255, b + 100)));
            }
        }
    }

    private bool TryParseColor(string colorString, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(colorString);
            return true;
        }
        catch
        {
            _logger.LogDebug("Invalid color: '{ColorString}'", colorString);
            color = default;
            return false;
        }
    }

    private void UpdateCurrentPersonalizationDisplay()
    {
        var lines = new List<string>
        {
            $"Theme: {_appSettings.SelectedTheme}",
            $"Primary: {_appSettings.PrimaryColor}  Accent: {_appSettings.AccentColor}",
            $"Border: {_appSettings.BorderColor}  Radius: {_appSettings.BorderRadius}px"
        };
        if (!string.IsNullOrEmpty(_appSettings.SecondaryColor)) lines.Add($"Surface: {_appSettings.SecondaryColor}");
        if (!string.IsNullOrEmpty(_appSettings.TextColor)) lines.Add($"Text: {_appSettings.TextColor}");
        lines.Add($"Font: {_appSettings.FontFamily} @ {_appSettings.FontSize}pt");
        lines.Add($"Width: {_appSettings.WindowWidth}  Position: {_appSettings.WindowVerticalPosition}");
        lines.Add($"Animations: {(_appSettings.AnimationsEnabled ? "On" : "Off")}  Compact: {(_appSettings.CompactMode ? "On" : "Off")}  Bubbles: {_appSettings.ChatBubbleStyle}");

        // Advanced personalization
        if (_appSettings.BackgroundType != "Solid")
            lines.Add($"Background: {_appSettings.BackgroundType}  Angle: {_appSettings.GradientAngle}°");

        lines.Add($"Placeholder: \"{_appSettings.CompassBoxDefaultText}\"");
        CurrentPersonalizationSettings.Text = string.Join("\n", lines);
    }

    private void SyncPersonalizationControls()
    {
        _isSyncingPersonalization = true;
        try
        {
            SyncColorSwatches();
            BorderRadiusSlider.Value = _appSettings.BorderRadius;
            WindowWidthSlider.Value = _appSettings.WindowWidth;
            FontSizeSlider.Value = _appSettings.FontSize;
            AnimationsCheck.IsChecked = _appSettings.AnimationsEnabled;
            CompactModeCheck.IsChecked = _appSettings.CompactMode;
            PlaceholderTextInput.Text = _appSettings.CompassBoxDefaultText;

            // Font family combo
            FontFamilyCombo.ItemsSource = new[]
            {
                "Segoe UI Variable Display, Segoe UI",
                "Segoe UI",
                "Cascadia Code",
                "Consolas",
                "Inter",
                "Arial",
                "Calibri"
            };
            FontFamilyCombo.SelectedItem = _appSettings.FontFamily;
            if (FontFamilyCombo.SelectedItem == null)
                FontFamilyCombo.SelectedIndex = 0;

            // Background effects
            var bgTypes = new[] { "Solid", "LinearGradient", "RadialGradient" };
            BackgroundTypeCombo.ItemsSource = bgTypes;
            BackgroundTypeCombo.SelectedItem = _appSettings.BackgroundType;
            if (BackgroundTypeCombo.SelectedItem == null) BackgroundTypeCombo.SelectedIndex = 0;

            GradientStartInput.Text = _appSettings.GradientStartColor;
            GradientEndInput.Text = _appSettings.GradientEndColor;
            GradientAngleSlider.Value = _appSettings.GradientAngle;

            // Window effects
            BorderThicknessSlider.Value = _appSettings.BorderThickness;
        }
        finally
        {
            _isSyncingPersonalization = false;
        }
    }

    private void BackupCurrentSettings()
    {
        if (_settingsBackup != null) return; // Don't overwrite a backup with temporary state
        _settingsBackup = new AppSettings();
        _settingsBackup.CopyPersonalizationFrom(_appSettings);
    }

    private void RestoreSettingsBackup()
    {
        if (_settingsBackup == null) return;
        _appSettings.CopyPersonalizationFrom(_settingsBackup);
        _settingsBackup = null;
        ApplyPersonalizationSettings();
        SyncPersonalizationControls();
    }

    private void QuickStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string styleDescription)
            PersonalizationInputBox.Text = styleDescription;
    }

    // --- Color swatch/input handlers ---

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border swatch && swatch.Tag is string colorType)
        {
            // Cycle through preset colors on click
            string[] presets = colorType switch
            {
                "Primary" => new[] { "#1C1C1C", "#0D1117", "#1A1B26", "#F5F5F5", "#FAFAFA", "#000000" },
                "Accent" => new[] { "#4CC2FF", "#0078D4", "#7C3AED", "#10B981", "#F59E0B", "#EF4444", "#EC4899", "#FFFF00" },
                "Border" => new[] { "#2D2D2D", "#3A3A3A", "#444444", "#D0D0D0", "#FFFFFF", "#000000" },
                "Secondary" => new[] { "#151515", "#1E1E2E", "#0D1117", "#EBEBEB", "#E0E0E0", "" },
                "Text" => new[] { "#F0F0F0", "#FFFFFF", "#E0E0E0", "#1A1A1A", "#333333", "" },
                _ => Array.Empty<string>()
            };

            string current = colorType switch
            {
                "Primary" => _appSettings.PrimaryColor,
                "Accent" => _appSettings.AccentColor,
                "Border" => _appSettings.BorderColor,
                "Secondary" => _appSettings.SecondaryColor,
                "Text" => _appSettings.TextColor,
                _ => ""
            };

            int idx = Array.IndexOf(presets, current);
            string next = presets[(idx + 1) % presets.Length];

            ApplyColorSetting(colorType, next);
        }
    }

    private void ColorInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is string colorType)
        {
            ApplyColorSetting(colorType, tb.Text.Trim());
        }
    }

    private void ApplyColorSetting(string colorType, string value)
    {
        // Allow empty for optional fields
        if (colorType is "Secondary" or "Text" && string.IsNullOrWhiteSpace(value))
        {
            switch (colorType)
            {
                case "Secondary": _appSettings.SecondaryColor = ""; break;
                case "Text": _appSettings.TextColor = ""; break;
            }
            ApplyPersonalizationSettings();
            SyncColorSwatches();
            SaveSettings();
            return;
        }

        if (!TryParseColor(value, out _)) return;

        switch (colorType)
        {
            case "Primary": _appSettings.PrimaryColor = value; break;
            case "Accent": _appSettings.AccentColor = value; break;
            case "Border": _appSettings.BorderColor = value; break;
            case "Secondary": _appSettings.SecondaryColor = value; break;
            case "Text": _appSettings.TextColor = value; break;
        }

        _appSettings.SelectedTheme = "Custom";
        ApplyPersonalizationSettings();
        SyncColorSwatches();
        SaveSettings();
    }

    private void SyncColorSwatches()
    {
        PrimaryColorInput.Text = _appSettings.PrimaryColor;
        AccentColorInput.Text = _appSettings.AccentColor;
        BorderColorInput.Text = _appSettings.BorderColor;
        SecondaryColorInput.Text = _appSettings.SecondaryColor;
        TextColorInput.Text = _appSettings.TextColor;

        if (TryParseColor(_appSettings.PrimaryColor, out var pc)) PrimaryColorSwatch.Background = new SolidColorBrush(pc);
        if (TryParseColor(_appSettings.AccentColor, out var ac)) AccentColorSwatch.Background = new SolidColorBrush(ac);
        if (TryParseColor(_appSettings.BorderColor, out var bc)) BorderColorSwatch.Background = new SolidColorBrush(bc);
        SecondaryColorSwatch.Background = TryParseColor(_appSettings.SecondaryColor, out var sc) ? new SolidColorBrush(sc) : Resources["SurfaceBrush"] as Brush;
        TextColorSwatch.Background = TryParseColor(_appSettings.TextColor, out var tc) ? new SolidColorBrush(tc) : Resources["TextPrimaryBrush"] as Brush;
    }

    // --- Slider handlers ---

    private DispatcherTimer? _personalizationSaveTimer;

    private void DebounceSavePersonalization()
    {
        if (_personalizationSaveTimer == null)
        {
            _personalizationSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _personalizationSaveTimer.Tick += (s, a) => { _personalizationSaveTimer.Stop(); SaveSettings(); };
        }
        _personalizationSaveTimer.Stop();
        _personalizationSaveTimer.Start();
    }

    private void BorderRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.BorderRadius = Math.Round(BorderRadiusSlider.Value);
        MainBorder.CornerRadius = new CornerRadius(_appSettings.BorderRadius);
        DebounceSavePersonalization();
    }

    private DispatcherTimer? _windowWidthTimer;
    private void WindowWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.WindowWidth = Math.Round(WindowWidthSlider.Value);

        // Debounce the actual window resize to avoid twitchy feedback loop
        // (resizing the window re-layouts the slider, which re-fires this event)
        if (_windowWidthTimer == null)
        {
            _windowWidthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _windowWidthTimer.Tick += (s, a) =>
            {
                _windowWidthTimer.Stop();
                this.Width = _appSettings.WindowWidth;
            };
        }
        _windowWidthTimer.Stop();
        _windowWidthTimer.Start();
        DebounceSavePersonalization();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.FontSize = Math.Round(FontSizeSlider.Value);
        this.FontSize = _appSettings.FontSize;
        DebounceSavePersonalization();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingPersonalization || FontFamilyCombo.SelectedItem == null) return;
        _appSettings.FontFamily = FontFamilyCombo.SelectedItem.ToString()!;
        this.FontFamily = new FontFamily(_appSettings.FontFamily);
        SaveSettings();
    }

    // --- Checkbox handlers ---

    private void AnimationsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.AnimationsEnabled = AnimationsCheck.IsChecked == true;
        SaveSettings();
    }

    private void CompactModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.CompactMode = CompactModeCheck.IsChecked == true;
        ApplyCompactMode();
        SaveSettings();
    }

    private void ApplyCompactMode()
    {
        double padding = _appSettings.CompactMode ? 10 : 14;
        double margin = _appSettings.CompactMode ? 10 : 15;
        SearchBarBorder.Padding = new Thickness(padding, padding - 2, padding, padding - 2);
        SearchBarBorder.Margin = new Thickness(margin);
    }

    // --- Background effects handlers ---

    private void BackgroundTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingPersonalization || BackgroundTypeCombo.SelectedItem == null) return;
        _appSettings.BackgroundType = BackgroundTypeCombo.SelectedItem.ToString()!;
        ApplyPersonalizationSettings();
        SaveSettings();
    }


    private void GradientColorInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.GradientStartColor = GradientStartInput.Text.Trim();
        _appSettings.GradientEndColor = GradientEndInput.Text.Trim();
        ApplyPersonalizationSettings();
        SaveSettings();
    }

    private void GradientAngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.GradientAngle = Math.Round(GradientAngleSlider.Value);
        ApplyPersonalizationSettings();
        DebounceSavePersonalization();
    }


    private void BorderThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _isSyncingPersonalization) return;
        _appSettings.BorderThickness = Math.Round(BorderThicknessSlider.Value, 1);
        ApplyPersonalizationSettings();
        DebounceSavePersonalization();
    }


    // --- Position & Style handlers ---

    private void WindowPosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string pos)
        {
            _appSettings.WindowVerticalPosition = pos;
            SaveSettings();
            PositionOnActiveMonitor();
        }
    }

    private void BubbleStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string style)
        {
            _appSettings.ChatBubbleStyle = style;
            SaveSettings();
        }
    }

    private void PlaceholderTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(PlaceholderTextInput.Text))
        {
            _appSettings.CompassBoxDefaultText = PlaceholderTextInput.Text.Trim();
            PlaceholderText.Text = _appSettings.CompassBoxDefaultText;
            SaveSettings();
        }
    }

    private async void ResetPersonalization_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ShowModernDialog("Reset Defaults",
            "Are you sure you want to reset Compass to default appearance?", "Yes", "No");
        if (confirmed)
        {
            _appSettings.CopyPersonalizationFrom(new AppSettings());
            _settingsBackup = null;
            PreviewSection.Visibility = Visibility.Collapsed;
            _currentPersonalizationProposal = null;
            PersonalizationInputBox.Clear();
            SaveSettings();
            ApplyPersonalizationSettings();
            SyncPersonalizationControls();
            await ShowModernDialog("Reset Complete", "Compass appearance has been reset to defaults.");
        }
    }

    private void BuiltInTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string themeName)
        {
            ApplyBuiltInTheme(themeName);
            SaveSettings();
        }
    }

    private void ApplyBuiltInTheme(string themeName)
    {
        // Reset all advanced settings to defaults first
        _appSettings.BackgroundType = "Solid";
        _appSettings.GradientStartColor = "";
        _appSettings.GradientEndColor = "";
        _appSettings.GradientAngle = 135.0;
        _appSettings.BorderThickness = 1.0;
        _appSettings.SecondaryColor = "";
        _appSettings.TextColor = "";
        _appSettings.CompactMode = false;
        _appSettings.AnimationsEnabled = true;

        switch (themeName)
        {
            case "Dark":
                _appSettings.PrimaryColor = "#1C1C1C";
                _appSettings.AccentColor = "#4CC2FF";
                _appSettings.BorderColor = "#2D2D2D";
                _appSettings.BorderRadius = 12;
                _appSettings.SelectedTheme = "Dark";
                break;
            case "Light":
                _appSettings.PrimaryColor = "#F5F5F5";
                _appSettings.AccentColor = "#0078D4";
                _appSettings.BorderColor = "#D0D0D0";
                _appSettings.BorderRadius = 12;
                _appSettings.SelectedTheme = "Light";
                break;
            case "High Contrast":
                _appSettings.PrimaryColor = "#000000";
                _appSettings.AccentColor = "#FFFF00";
                _appSettings.BorderColor = "#FFFFFF";
                _appSettings.BorderRadius = 0;
                _appSettings.AnimationsEnabled = false;
                _appSettings.SelectedTheme = "High Contrast";
                break;
        }
        ApplyPersonalizationSettings();
        SyncPersonalizationControls();
    }

    private async void GeneratePersonalizationPreview_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PersonalizationInputBox.Text))
        {
            await ShowModernDialog("Input Required", "Please describe how you want Compass to look.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
        {
            await ShowModernDialog("API Key Required", "Please configure your API Key in the General settings first.");
            return;
        }

        var btn = sender as Button;
        if (btn != null) { btn.IsEnabled = false; btn.Content = "Generating..."; }

        try
        {
            string systemPrompt = PersonalizationManager.GetPersonalizationSystemPrompt();
            string userRequest = PersonalizationInputBox.Text;

            // Use a fresh single-turn request (not chat history)
            var tempSettings = new AppSettings
            {
                ApiKey = _appSettings.ApiKey,
                SelectedModel = _appSettings.SelectedModel,
                SystemPrompt = systemPrompt
            };
            var aiResponse = await _geminiService.AskAsync(userRequest, tempSettings);
            string jsonText = aiResponse.Text;

            // Strip markdown fences if present
            if (jsonText.Contains("```"))
            {
                int start = jsonText.IndexOf("{");
                int end = jsonText.LastIndexOf("}");
                if (start >= 0 && end >= 0)
                    jsonText = jsonText.Substring(start, end - start + 1);
            }

            _currentPersonalizationProposal = PersonalizationManager.ParseResponse(jsonText);

            if (_currentPersonalizationProposal == null ||
                (string.IsNullOrEmpty(_currentPersonalizationProposal.CompassBoxDefaultText) &&
                 string.IsNullOrEmpty(_currentPersonalizationProposal.PrimaryColor) &&
                 string.IsNullOrEmpty(_currentPersonalizationProposal.AccentColor) &&
                 string.IsNullOrEmpty(_currentPersonalizationProposal.BackgroundType) &&
                 _currentPersonalizationProposal.WindowWidth == null &&
                 _currentPersonalizationProposal.FontFamily == null))
            {
                await ShowModernDialog("Invalid Request", "Could not understand your request. Please describe visual changes more clearly.");
                return;
            }

            BackupCurrentSettings();
            ApplyProposalTemporarily(_currentPersonalizationProposal);
            ShowPersonalizationPreview(_currentPersonalizationProposal);
        }
        catch (Exception ex)
        {
            await ShowModernDialog("Error", $"Error generating preview: {ex.Message}");
        }
        finally
        {
            if (btn != null) { btn.IsEnabled = true; btn.Content = "Generate & Preview"; }
        }
    }

    private void ApplyProposalTemporarily(PersonalizationProposal proposal)
    {
        proposal.ApplyTo(_appSettings);
        ApplyPersonalizationSettings();
    }

    private void ShowPersonalizationPreview(PersonalizationProposal proposal)
    {
        var changesList = new List<string>();
        if (!string.IsNullOrEmpty(proposal.CompassBoxDefaultText))
            changesList.Add($"• Default text: \"{proposal.CompassBoxDefaultText}\"");
        if (!string.IsNullOrEmpty(proposal.PrimaryColor))
            changesList.Add($"• Primary color: {proposal.PrimaryColor}");
        if (!string.IsNullOrEmpty(proposal.AccentColor))
            changesList.Add($"• Accent color: {proposal.AccentColor}");
        if (!string.IsNullOrEmpty(proposal.SecondaryColor))
            changesList.Add($"• Surface color: {proposal.SecondaryColor}");
        if (!string.IsNullOrEmpty(proposal.TextColor))
            changesList.Add($"• Text color: {proposal.TextColor}");
        if (proposal.WindowWidth.HasValue)
            changesList.Add($"• Window width: {proposal.WindowWidth}");
        if (!string.IsNullOrEmpty(proposal.FontFamily))
            changesList.Add($"• Font: {proposal.FontFamily}");
        if (proposal.FontSize.HasValue)
            changesList.Add($"• Font size: {proposal.FontSize}");
        if (proposal.AnimationsEnabled.HasValue)
            changesList.Add($"• Animations: {(proposal.AnimationsEnabled.Value ? "Enabled" : "Disabled")}");
        if (!string.IsNullOrEmpty(proposal.BorderColor))
            changesList.Add($"• Border color: {proposal.BorderColor}");
        if (proposal.BorderRadius.HasValue)
            changesList.Add($"• Corner radius: {proposal.BorderRadius}");
        if (proposal.CompactMode.HasValue)
            changesList.Add($"• Compact mode: {(proposal.CompactMode.Value ? "On" : "Off")}");

        // Advanced properties
        if (!string.IsNullOrEmpty(proposal.BackgroundType))
            changesList.Add($"• Background: {proposal.BackgroundType}");
        if (!string.IsNullOrEmpty(proposal.GradientStartColor))
            changesList.Add($"• Gradient: {proposal.GradientStartColor} → {proposal.GradientEndColor}");
        if (proposal.GradientAngle.HasValue)
            changesList.Add($"• Gradient angle: {proposal.GradientAngle}°");
        if (proposal.BorderThickness.HasValue)
            changesList.Add($"• Border thickness: {proposal.BorderThickness}px");

        PreviewChangesText.Text = string.Join("\n", changesList);
        PreviewSection.Visibility = Visibility.Visible;
    }

    private async void AcceptPersonalization_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPersonalizationProposal == null) return;
        SaveSettings();
        _settingsBackup = null;
        PreviewSection.Visibility = Visibility.Collapsed;
        PersonalizationInputBox.Clear();
        _currentPersonalizationProposal = null;
        SyncPersonalizationControls();
        await ShowModernDialog("Success", "Personalization changes applied successfully!");
    }

    private void RejectPersonalization_Click(object sender, RoutedEventArgs e)
    {
        RestoreSettingsBackup();
        PreviewSection.Visibility = Visibility.Collapsed;
        _currentPersonalizationProposal = null;
    }

}
