using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Compass;

/// <summary>
/// MainWindow - Chat UI and AI integration
/// </summary>
public partial class MainWindow
{
    // ---------------------------------------------------------------------------
    // Gemini / AI
    // ---------------------------------------------------------------------------

    private async Task AskGeminiAsync(string prompt, List<(byte[] data, string mimeType)>? images = null)
    {
        _chatCts?.Cancel();
        _chatCts = new CancellationTokenSource();
        var token = _chatCts.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
            {
                AddChatBubble("System", "API key is not set. Please enter a valid Gemini API key in settings.");
                return;
            }

            // RAG augmentation (Feature 6)
            string augmentedPrompt = prompt;
            if (_appSettings.RagEnabled && _ragService.ChunkCount > 0)
            {
                var relevantChunks = _ragService.RetrieveRelevant(prompt);
                if (relevantChunks.Count > 0)
                    augmentedPrompt = _ragService.BuildAugmentedPrompt(prompt, relevantChunks);
            }

            string selectedModel = _routingService.SelectModel(augmentedPrompt, _appSettings, images?.Count > 0);
            ShowTypingIndicator();
            var geminiResponse = await _geminiService.AskAsync(augmentedPrompt, _appSettings, token, selectedModel, images);
            RemoveTypingIndicator();

            if (!token.IsCancellationRequested)
            {
                string senderLabel = _appSettings.SmartRoutingEnabled
                    ? $"Gemini ({geminiResponse.ModelUsed})"
                    : "Gemini";

                if (geminiResponse.Images.Count > 0)
                    AddChatBubbleWithGeneratedImages(senderLabel, geminiResponse.Text, geminiResponse.Images);
                else
                    AddChatBubble(senderLabel, geminiResponse.Text);
            }
        }
        catch (OperationCanceledException) { RemoveTypingIndicator(); }
        catch (Exception ex)
        {
            RemoveTypingIndicator();
            if (!token.IsCancellationRequested)
                AddChatBubble("System", $"Error: {ex.Message}");
        }
        finally
        {
            InputBox.IsEnabled = true;
            InputBox.Focus();
        }
    }

    private async Task GenerateExtensionAsync(string intent, string name)
    {
        try
        {
            AddChatBubble("System", $"Generating command '{name}'...");

            if (string.IsNullOrWhiteSpace(_appSettings.ApiKey))
            {
                AddChatBubble("System", "API key is not set. Please enter a valid Gemini API key in settings.");
                return;
            }

            string script = await _geminiService.GeneratePowerShellScriptAsync(intent, _appSettings);
            var ext = new CompassExtension { TriggerName = name, Description = intent, PowerShellScript = script };
            _extService.SaveExtension(ext);
            await RefreshExtensionCacheAsync();
            AddChatBubble("System", $"Command '{name}' created successfully.");
        }
        catch (Exception ex)
        {
            AddChatBubble("System", $"Failed to generate extension: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // Chat UI
    // ---------------------------------------------------------------------------

    private void AddChatBubble(string sender, string text)
    {
        var outerPanel = new StackPanel
        {
            Margin = new Thickness(10, 2, 10, 6),
            MaxWidth = 520
        };

        bool isUser = sender == "You";
        outerPanel.HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        // Sender label
        var senderLabel = new TextBlock
        {
            Text = sender,
            FontSize = 11,
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(isUser ? 0 : 4, 0, isUser ? 4 : 0, 4),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };

        if (isUser)
            senderLabel.Foreground = Resources["TextTertiaryBrush"] as Brush ?? Brushes.Gray;
        else if (sender.StartsWith("Gemini"))
            senderLabel.Foreground = Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue;
        else
            senderLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x35)); // amber for System

        outerPanel.Children.Add(senderLabel);

        bool rounded = _appSettings.ChatBubbleStyle != "Square";
        var border = new Border
        {
            CornerRadius = rounded
                ? (isUser ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4))
                : new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10)
        };

        if (isUser)
        {
            border.Background = Resources["CardBrush"] as Brush ?? Brushes.DarkGray;
        }
        else
        {
            border.Background = Resources["SurfaceBrush"] as Brush ?? Brushes.Black;
            border.BorderBrush = Resources["InputBorderBrush"] as Brush ?? Brushes.Gray;
            border.BorderThickness = new Thickness(1);
        }

        // Build the message content — detect code blocks
        var contentPanel = new StackPanel();
        var segments = SplitCodeBlocks(text);
        foreach (var (content, isCode) in segments)
        {
            if (isCode)
            {
                bool isDark = _appSettings.SelectedTheme != "Light";
                var codeBlock = new Border
                {
                    Background = new SolidColorBrush(isDark ? Color.FromRgb(0x0D, 0x0D, 0x0D) : Color.FromRgb(0xF0, 0xF0, 0xF0)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                var codeText = new TextBox
                {
                    Text = content.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                    FontSize = 12.5,
                    Foreground = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.IBeam,
                    CaretBrush = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White
                };
                codeBlock.Child = codeText;
                contentPanel.Children.Add(codeBlock);
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                var textBox = new TextBox
                {
                    Text = content.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Segoe UI Variable Text"),
                    FontSize = 14,
                    Foreground = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.IBeam,
                    CaretBrush = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White
                };
                contentPanel.Children.Add(textBox);
            }
        }

        border.Child = contentPanel;
        outerPanel.Children.Add(border);
        ChatPanel.Children.Add(outerPanel);

        // Fade-in + slide-up animation
        if (_appSettings.AnimationsEnabled)
        {
            outerPanel.Opacity = 0;
            var translate = new TranslateTransform(0, 12);
            outerPanel.RenderTransform = translate;
            outerPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        ChatScroll.ScrollToBottom();
    }

    /// <summary>
    /// Splits text into alternating (text, false) and (code, true) segments using ``` fences.
    /// </summary>
    private static List<(string content, bool isCode)> SplitCodeBlocks(string text)
    {
        var result = new List<(string, bool)>();
        int idx = 0;
        while (idx < text.Length)
        {
            int start = text.IndexOf("```", idx);
            if (start < 0)
            {
                result.Add((text.Substring(idx), false));
                break;
            }

            if (start > idx)
                result.Add((text.Substring(idx, start - idx), false));

            // Skip opening ``` and optional language tag on same line
            int codeStart = text.IndexOf('\n', start);
            if (codeStart < 0) codeStart = start + 3;
            else codeStart++;

            int end = text.IndexOf("```", codeStart);
            if (end < 0)
            {
                result.Add((text.Substring(codeStart), true));
                break;
            }

            result.Add((text.Substring(codeStart, end - codeStart), true));
            idx = end + 3;
            if (idx < text.Length && text[idx] == '\n') idx++;
        }
        return result;
    }

    // --- Typing indicator ---

    private Border? _typingIndicator;

    private void ShowTypingIndicator()
    {
        _typingIndicator = new Border
        {
            CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(16, 10, 16, 10),
            Background = Resources["SurfaceBrush"] as Brush ?? Brushes.Black,
            BorderBrush = Resources["InputBorderBrush"] as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 2, 10, 6),
            MaxWidth = 520
        };

        var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < 3; i++)
        {
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Resources["TextTertiaryBrush"] as Brush ?? Brushes.Gray,
                Margin = new Thickness(2, 0, 2, 0),
                Opacity = 0.3
            };

            if (_appSettings.AnimationsEnabled)
            {
                var pulse = new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(0.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 200)
                };
                dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            else
            {
                dot.Opacity = 0.6;
            }

            dotsPanel.Children.Add(dot);
        }

        _typingIndicator.Child = dotsPanel;
        ChatPanel.Children.Add(_typingIndicator);

        if (_appSettings.AnimationsEnabled)
        {
            _typingIndicator.Opacity = 0;
            _typingIndicator.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
        }

        ChatScroll.ScrollToBottom();
    }

    private void RemoveTypingIndicator()
    {
        if (_typingIndicator != null)
        {
            ChatPanel.Children.Remove(_typingIndicator);
            _typingIndicator = null;
        }
    }

    // ---------------------------------------------------------------------------
    // Image attachment helpers
    // ---------------------------------------------------------------------------

    private static byte[] BitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static bool IsImageFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }

    private static string GetMimeType(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private void UpdateAttachedImagesUI()
    {
        AttachedImagesPanel.Children.Clear();
        if (_pendingImages.Count == 0)
        {
            AttachedImagesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AttachedImagesPanel.Visibility = Visibility.Visible;
        AttachedImagesPanel.Margin = new Thickness(0, 0, 0, 8);

        foreach (var (data, mimeType, fileName) in _pendingImages)
        {
            var img = new System.Windows.Controls.Image
            {
                Width = 28,
                Height = 28,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"{fileName} (click to remove)"
            };

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Child = img
            };

            // Click to remove
            var capturedItem = (data, mimeType, fileName);
            border.MouseLeftButtonUp += (s, e) =>
            {
                _pendingImages.Remove(capturedItem);
                UpdateAttachedImagesUI();
            };

            AttachedImagesPanel.Children.Add(border);
        }
    }

    private void InputBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files.Any(f => IsImageFile(f)))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void InputBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        foreach (string file in files)
        {
            if (IsImageFile(file))
            {
                byte[] data = File.ReadAllBytes(file);
                _pendingImages.Add((data, GetMimeType(file), System.IO.Path.GetFileName(file)));
            }
        }
        UpdateAttachedImagesUI();
    }

    private void AttachImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
            Multiselect = true,
            Title = "Attach Images"
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (string file in dlg.FileNames)
            {
                if (IsImageFile(file))
                {
                    byte[] data = File.ReadAllBytes(file);
                    _pendingImages.Add((data, GetMimeType(file), System.IO.Path.GetFileName(file)));
                }
            }
            UpdateAttachedImagesUI();
        }
    }

    // ---------------------------------------------------------------------------
    // Chat bubbles with images
    // ---------------------------------------------------------------------------

    private void AddChatBubbleWithImages(string sender, string text, List<byte[]> imageDataList)
    {
        var outerPanel = new StackPanel
        {
            Margin = new Thickness(10, 2, 10, 6),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var senderLabel = new TextBlock
        {
            Text = sender,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 4, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Resources["TextTertiaryBrush"] as Brush ?? Brushes.Gray
        };
        outerPanel.Children.Add(senderLabel);

        // Image thumbnails
        var imagesPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var data in imageDataList)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 200;
            bmp.EndInit();
            bmp.Freeze();

            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                MaxWidth = 200,
                MaxHeight = 150,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 4, 4)
            };
            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = img
            };
            imagesPanel.Children.Add(imgBorder);
        }
        outerPanel.Children.Add(imagesPanel);

        // Text bubble
        if (!string.IsNullOrWhiteSpace(text))
        {
            bool rounded = _appSettings.ChatBubbleStyle != "Square";
            var border = new Border
            {
                CornerRadius = rounded ? new CornerRadius(12, 12, 4, 12) : new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Background = Resources["CardBrush"] as Brush ?? Brushes.DarkGray
            };

            var textBox = new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI Variable Text"),
                FontSize = 14,
                Foreground = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.IBeam,
                CaretBrush = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White
            };
            border.Child = textBox;
            outerPanel.Children.Add(border);
        }

        ChatPanel.Children.Add(outerPanel);
        if (_appSettings.AnimationsEnabled)
        {
            outerPanel.Opacity = 0;
            var translate = new TranslateTransform(0, 12);
            outerPanel.RenderTransform = translate;
            outerPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        ChatScroll.ScrollToBottom();
    }

    private void AddChatBubbleWithGeneratedImages(string sender, string text, List<(byte[] data, string mimeType)> images)
    {
        var outerPanel = new StackPanel
        {
            Margin = new Thickness(10, 2, 10, 6),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var senderLabel = new TextBlock
        {
            Text = sender,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = sender.StartsWith("Gemini")
                ? (Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue)
                : new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x35))
        };
        outerPanel.Children.Add(senderLabel);

        bool rounded = _appSettings.ChatBubbleStyle != "Square";
        var border = new Border
        {
            CornerRadius = rounded ? new CornerRadius(12, 12, 12, 4) : new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
            Background = Resources["SurfaceBrush"] as Brush ?? Brushes.Black,
            BorderBrush = Resources["InputBorderBrush"] as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1)
        };

        var contentPanel = new StackPanel();

        // Text first
        if (!string.IsNullOrWhiteSpace(text))
        {
            var segments = SplitCodeBlocks(text);
            foreach (var (content, isCode) in segments)
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var textBox = new TextBox
                    {
                        Text = content.Trim(),
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = isCode ? new FontFamily("Cascadia Code, Consolas, Courier New") : new FontFamily("Segoe UI Variable Text"),
                        FontSize = isCode ? 12.5 : 14,
                        Foreground = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
                        IsReadOnly = true,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Cursor = Cursors.IBeam,
                        CaretBrush = Resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    contentPanel.Children.Add(textBox);
                }
            }
        }

        // Generated images
        foreach (var (data, mimeType) in images)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                MaxWidth = 400,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 6, 0, 4),
                Cursor = Cursors.Hand
            };
            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = img
            };

            // Right-click to save
            var capturedData = data;
            var capturedMime = mimeType;
            img.MouseRightButtonUp += (s, e) =>
            {
                string ext = capturedMime switch
                {
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/webp" => ".webp",
                    _ => ".png"
                };
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"compass_image{ext}",
                    Filter = $"Image file|*{ext}|All files|*.*"
                };
                if (dlg.ShowDialog() == true)
                    File.WriteAllBytes(dlg.FileName, capturedData);
            };

            contentPanel.Children.Add(imgBorder);
        }

        border.Child = contentPanel;
        outerPanel.Children.Add(border);
        ChatPanel.Children.Add(outerPanel);

        if (_appSettings.AnimationsEnabled)
        {
            outerPanel.Opacity = 0;
            var translate = new TranslateTransform(0, 12);
            outerPanel.RenderTransform = translate;
            outerPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        ChatScroll.ScrollToBottom();
    }

    private void AnimateOrSnap(IAnimatable target, DependencyProperty property, double to, TimeSpan duration, IEasingFunction? easing = null, Action? onCompleted = null)
    {
        if (_appSettings.AnimationsEnabled)
        {
            var anim = new DoubleAnimation(to, duration) { EasingFunction = easing };
            if (onCompleted != null)
                anim.Completed += (s, e) => onCompleted();
            target.BeginAnimation(property, anim);
        }
        else
        {
            target.BeginAnimation(property, null); // clear any running animation
            if (target is DependencyObject depObj)
                ((DependencyObject)target).SetValue(property, to);
            onCompleted?.Invoke();
        }
    }

    private void AnimateToChatMode()
    {
        HideWidgetPanel();
        HideSearchList();
        ExitChatArrow.Visibility = Visibility.Visible;
        ClearChatBtn.Visibility = Visibility.Visible;
        ExportChatBtn.Visibility = Visibility.Visible;
        PinBtn.Visibility = Visibility.Visible;
        ChatScroll.Visibility = Visibility.Visible;

        // Staggered entrance: opacity leads, scale follows with spring-like easing
        AnimateOrSnap(ChatScroll, UIElement.OpacityProperty, 1, TimeSpan.FromSeconds(0.25));
        AnimateOrSnap(ChatScale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromSeconds(0.4),
            new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 8 });
        UpdateResumeIndicator();
    }

    private void AnimateToSpotlightMode()
    {
        HideSearchList();
        ExitChatArrow.Visibility = Visibility.Collapsed;
        ClearChatBtn.Visibility = Visibility.Collapsed;
        ExportChatBtn.Visibility = Visibility.Collapsed;
        PinBtn.Visibility = Visibility.Collapsed;

        AnimateOrSnap(ChatScale, ScaleTransform.ScaleYProperty, 0, TimeSpan.FromSeconds(0.25),
            new CubicEase { EasingMode = EasingMode.EaseIn },
            () =>
            {
                ChatScroll.Visibility = Visibility.Collapsed;
                UpdateResumeIndicator();
            });
    }

    private void UpdateResumeIndicator()
    {
        if (ChatScroll.Visibility == Visibility.Visible)
        {
            ResumeChatIndicator.Visibility = Visibility.Collapsed;
            return;
        }
        bool hasHistory = _geminiService.HasHistory;
        bool isEmpty = string.IsNullOrEmpty(InputBox.Text);
        ResumeChatIndicator.Visibility = (hasHistory && isEmpty) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResumeChat()
    {
        InputBox.Clear();
        AnimateToChatMode();
    }

    private void ResumeChat_Click(object sender, MouseButtonEventArgs e) => ResumeChat();

    private void ClearChatBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _chatCts?.Cancel();
        _geminiService.ClearHistory();
        ChatPanel.Children.Clear();
        AddChatBubble("System", "Chat history cleared.");
        UpdateResumeIndicator();
    }

    private void ExportChatBtn_Click(object sender, MouseButtonEventArgs e)
    {
        var history = _geminiService.GetExportableHistory();
        if (history.Count == 0)
        {
            AddChatBubble("System", "Nothing to export — chat is empty.");
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"Compass_Chat_{DateTime.Now:yyyy-MM-dd_HHmm}",
            Filter = "Markdown file|*.md|Text file|*.txt",
            Title = "Export Chat"
        };

        if (dlg.ShowDialog() == true)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Compass Chat Export");
            sb.AppendLine($"*{DateTime.Now:MMMM d, yyyy h:mm tt}*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var (role, text) in history)
            {
                sb.AppendLine($"**{role}:**");
                sb.AppendLine(text);
                sb.AppendLine();
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
            AddChatBubble("System", $"Chat exported to {System.IO.Path.GetFileName(dlg.FileName)}");
        }
    }

    private void ExitChatBtn_Click(object sender, MouseButtonEventArgs e)
    {
        AnimateToSpotlightMode();
        ExitChatArrow.Visibility = Visibility.Collapsed;
    }

}
