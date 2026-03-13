using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Compass.Services;
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
        _chatCts?.Dispose();
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

            // Detect if this is an image generation request (auto or manual toggle)
            bool isImageRequest = _imageGenModeEnabled || GeminiService.IsImageGenerationRequest(augmentedPrompt);
            bool hasImages = images?.Count > 0;

            string selectedModel;
            if (isImageRequest)
                selectedModel = _appSettings.ImageGenerationModel;
            else
                selectedModel = _routingService.SelectModel(augmentedPrompt, _appSettings, hasImages);

            // Use streaming for text-only (non-image) requests without image attachments
            bool useStreaming = !isImageRequest && !hasImages;

            if (useStreaming)
            {
                ShowTypingIndicator();

                // Prepare the streaming bubble shell
                string senderLabel = _appSettings.SmartRoutingEnabled ? "Gemini" : "Gemini";
                TextBox? streamTextBox = null;
                StackPanel? streamContentPanel = null;
                bool bubbleCreated = false;

                var geminiResponse = await _geminiService.AskStreamingAsync(
                    augmentedPrompt, _appSettings,
                    chunk => Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        if (!bubbleCreated)
                        {
                            RemoveTypingIndicator();
                            (streamTextBox, streamContentPanel) = CreateStreamingBubble(senderLabel);
                            bubbleCreated = true;
                        }

                        // Append chunk to the live text box
                        if (streamTextBox != null)
                        {
                            streamTextBox.Text += chunk;
                            ChatScroll.ScrollToBottom();
                        }
                    }),
                    token, selectedModel);

                if (!token.IsCancellationRequested)
                {
                    // Update sender label with model info if smart routing is on
                    if (_appSettings.SmartRoutingEnabled && bubbleCreated)
                        UpdateStreamingBubbleSender(streamContentPanel, $"Gemini ({geminiResponse.ModelUsed})");

                    // Re-render the bubble content with proper code block formatting
                    if (bubbleCreated && streamContentPanel != null)
                        FinalizeStreamingBubble(streamContentPanel, geminiResponse.Text);

                    if (!bubbleCreated)
                    {
                        // No chunks arrived (empty response) — show as normal bubble
                        RemoveTypingIndicator();
                        AddChatBubble(senderLabel, geminiResponse.Text);
                    }

                    AddRetryButton();
                    AutoSaveChatSession();
                }
            }
            else
            {
                // Non-streaming path for image requests / image attachments
                ShowTypingIndicator();

                GeminiResponse geminiResponse;
                if (isImageRequest && !GeminiService.IsImageGenerationRequest(augmentedPrompt))
                {
                    string imagePrompt = "Generate an image: " + augmentedPrompt;
                    geminiResponse = await _geminiService.AskAsync(imagePrompt, _appSettings, token, selectedModel, images);
                }
                else
                {
                    geminiResponse = await _geminiService.AskAsync(augmentedPrompt, _appSettings, token, selectedModel, images);
                }
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

                    AddRetryButton();
                    AutoSaveChatSession();
                }
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

        // Build the message content
        var contentPanel = new StackPanel();
        if (isUser)
        {
            // User messages: plain text
            var textBox = new TextBox
            {
                Text = text.Trim(),
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
        else
        {
            // AI/System messages: full markdown rendering
            var rendered = MarkdownRenderer.Render(text, Resources);
            foreach (var element in rendered)
                contentPanel.Children.Add(element);
        }

        border.Tag = text;
        border.Child = contentPanel;

        // Context menu: Copy Message (all bubbles) + Retry (AI bubbles only)
        var bubbleMenu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy Message" };
        copyItem.Click += (s, e) =>
        {
            if (border.Tag is string msg && !string.IsNullOrEmpty(msg))
                System.Windows.Clipboard.SetText(msg);
        };
        bubbleMenu.Items.Add(copyItem);

        if (!isUser && !sender.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            var retryItem = new MenuItem { Header = "Retry" };
            retryItem.Click += (s, e) =>
            {
                // Only allow retry on the most recent AI bubble
                var lastBubble = GetLastAiBubblePanel();
                if (lastBubble == outerPanel)
                    RetryLastMessage();
            };
            bubbleMenu.Items.Add(retryItem);
        }
        border.ContextMenu = bubbleMenu;

        outerPanel.Children.Add(border);
        ChatPanel.Children.Add(outerPanel);

        // Fade-in + slide-up + scale animation
        if (_appSettings.AnimationsEnabled)
        {
            outerPanel.Opacity = 0;
            outerPanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var transformGroup = new TransformGroup();
            var translate = new TranslateTransform(0, 12);
            var scale = new ScaleTransform(0.97, 0.97);
            transformGroup.Children.Add(translate);
            transformGroup.Children.Add(scale);
            outerPanel.RenderTransform = transformGroup;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromSeconds(0.3);
            outerPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease });
        }

        ChatScroll.ScrollToBottom();
    }

    /// <summary>
    /// Creates a chat bubble shell for streaming — returns a TextBox to append chunks to,
    /// and the content panel for later re-rendering with code block formatting.
    /// </summary>
    private (TextBox textBox, StackPanel contentPanel) CreateStreamingBubble(string sender)
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
            FontWeight = FontWeights.Normal,
            Foreground = Resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue,
            Margin = new Thickness(4, 0, 0, 4),
            Tag = "StreamingSenderLabel"
        };
        outerPanel.Children.Add(senderLabel);

        bool rounded = _appSettings.ChatBubbleStyle != "Square";
        var border = new Border
        {
            CornerRadius = rounded ? new CornerRadius(14, 14, 14, 4) : new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
            Background = Resources["SurfaceBrush"] as Brush ?? Brushes.Black,
            BorderBrush = Resources["InputBorderBrush"] as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1)
        };

        var contentPanel = new StackPanel();
        var streamTextBox = new TextBox
        {
            Text = "",
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
        contentPanel.Children.Add(streamTextBox);

        border.Tag = "";
        border.Child = contentPanel;

        // Context menu (Copy + Retry) — Tag will be updated after finalization
        var bubbleMenu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy Message" };
        copyItem.Click += (s, e) =>
        {
            if (border.Tag is string msg && !string.IsNullOrEmpty(msg))
                System.Windows.Clipboard.SetText(msg);
        };
        bubbleMenu.Items.Add(copyItem);

        var retryItem = new MenuItem { Header = "Retry" };
        retryItem.Click += (s, e) =>
        {
            var lastBubble = GetLastAiBubblePanel();
            if (lastBubble == outerPanel)
                RetryLastMessage();
        };
        bubbleMenu.Items.Add(retryItem);
        border.ContextMenu = bubbleMenu;

        outerPanel.Children.Add(border);
        ChatPanel.Children.Add(outerPanel);

        if (_appSettings.AnimationsEnabled)
        {
            outerPanel.Opacity = 0;
            outerPanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var streamTransformGroup = new TransformGroup();
            var translate = new TranslateTransform(0, 12);
            var scale = new ScaleTransform(0.97, 0.97);
            streamTransformGroup.Children.Add(translate);
            streamTransformGroup.Children.Add(scale);
            outerPanel.RenderTransform = streamTransformGroup;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromSeconds(0.3);
            outerPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1.0, dur) { EasingFunction = ease });
        }

        ChatScroll.ScrollToBottom();
        return (streamTextBox, contentPanel);
    }

    /// <summary>
    /// Updates the sender label on a streaming bubble (e.g., to add model name after response).
    /// </summary>
    private void UpdateStreamingBubbleSender(StackPanel? contentPanel, string newSender)
    {
        if (contentPanel == null) return;
        // The sender label is in the outer panel (parent of the border that contains contentPanel)
        var border = contentPanel.Parent as Border;
        var outerPanel = border?.Parent as StackPanel;
        if (outerPanel == null) return;

        foreach (var child in outerPanel.Children)
        {
            if (child is TextBlock tb && tb.Tag as string == "StreamingSenderLabel")
            {
                tb.Text = newSender;
                break;
            }
        }
    }

    /// <summary>
    /// Re-renders the streaming bubble content with proper code block formatting.
    /// </summary>
    private void FinalizeStreamingBubble(StackPanel contentPanel, string fullText)
    {
        contentPanel.Children.Clear();

        var rendered = MarkdownRenderer.Render(fullText, Resources);
        foreach (var element in rendered)
            contentPanel.Children.Add(element);

        // Update the border's Tag with the full response text for Copy Message
        if (contentPanel.Parent is Border border)
            border.Tag = fullText;
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

        _isFileDialogOpen = true;
        try
        {
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
        finally
        {
            _isFileDialogOpen = false;
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
                MaxWidth = 480,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 6, 0, 2),
                Cursor = Cursors.Hand
            };
            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = img
            };

            // Click-to-zoom: left click shows full-size overlay
            var capturedBmp = bmp;
            img.MouseLeftButtonUp += (s, e) =>
            {
                ShowImageOverlay(capturedBmp);
                e.Handled = true;
            };

            // Right-click to save (kept for backward compat)
            var capturedData = data;
            var capturedMime = mimeType;
            img.MouseRightButtonUp += (s, e) =>
            {
                SaveGeneratedImage(capturedData, capturedMime);
                e.Handled = true;
            };

            contentPanel.Children.Add(imgBorder);

            // Visible "Save Image" button
            var saveBtn = new Border
            {
                Background = Resources["CardBrush"] as Brush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 2, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            saveBtn.Child = new TextBlock
            {
                Text = "\uD83D\uDCBE Save Image",
                FontSize = 11,
                Foreground = Resources["TextSecondaryBrush"] as Brush
            };
            var saveCapturedData = data;
            var saveCapturedMime = mimeType;
            saveBtn.MouseLeftButtonUp += (s, e) =>
            {
                SaveGeneratedImage(saveCapturedData, saveCapturedMime);
                e.Handled = true;
            };
            contentPanel.Children.Add(saveBtn);
        }

        border.Tag = text;
        border.Child = contentPanel;

        // Context menu: Copy Message + Retry
        var genImgMenu = new ContextMenu();
        var genCopyItem = new MenuItem { Header = "Copy Message" };
        genCopyItem.Click += (s, e) =>
        {
            if (border.Tag is string msg && !string.IsNullOrEmpty(msg))
                System.Windows.Clipboard.SetText(msg);
        };
        genImgMenu.Items.Add(genCopyItem);

        var genRetryItem = new MenuItem { Header = "Retry" };
        genRetryItem.Click += (s, e) =>
        {
            var lastBubble = GetLastAiBubblePanel();
            if (lastBubble == outerPanel)
                RetryLastMessage();
        };
        genImgMenu.Items.Add(genRetryItem);
        border.ContextMenu = genImgMenu;

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

    // ---------------------------------------------------------------------------
    // Retry
    // ---------------------------------------------------------------------------

    private void AddRetryButton()
    {
        RemoveRetryButton();

        var retryBorder = new Border
        {
            Tag = "RetryButton",
            Background = Resources["CardBrush"] as Brush ?? Brushes.DarkGray,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(14, 0, 0, 6)
        };
        retryBorder.Child = new TextBlock
        {
            Text = "\u21BB Retry",
            FontSize = 11,
            Foreground = Resources["TextSecondaryBrush"] as Brush ?? Brushes.LightGray
        };
        retryBorder.MouseEnter += (s, e) => retryBorder.Background = Resources["HoverBrush"] as Brush ?? Brushes.Gray;
        retryBorder.MouseLeave += (s, e) => retryBorder.Background = Resources["CardBrush"] as Brush ?? Brushes.DarkGray;
        retryBorder.MouseLeftButtonUp += (s, e) =>
        {
            RetryLastMessage();
            e.Handled = true;
        };

        ChatPanel.Children.Add(retryBorder);
        ChatScroll.ScrollToBottom();
    }

    private void RemoveRetryButton()
    {
        for (int i = ChatPanel.Children.Count - 1; i >= 0; i--)
        {
            if (ChatPanel.Children[i] is Border b && b.Tag as string == "RetryButton")
            {
                ChatPanel.Children.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Finds the outermost StackPanel of the last AI bubble in the chat.
    /// </summary>
    private StackPanel? GetLastAiBubblePanel()
    {
        for (int i = ChatPanel.Children.Count - 1; i >= 0; i--)
        {
            if (ChatPanel.Children[i] is StackPanel sp && sp.HorizontalAlignment == HorizontalAlignment.Left)
                return sp;
        }
        return null;
    }

    private void RetryLastMessage()
    {
        // Find the last user bubble (HorizontalAlignment.Right)
        string? userText = null;
        int userIndex = -1;
        for (int i = ChatPanel.Children.Count - 1; i >= 0; i--)
        {
            if (ChatPanel.Children[i] is StackPanel sp && sp.HorizontalAlignment == HorizontalAlignment.Right)
            {
                // Extract text from the border's Tag
                foreach (var child in sp.Children)
                {
                    if (child is Border b && b.Tag is string tag && !string.IsNullOrEmpty(tag))
                    {
                        userText = tag;
                        break;
                    }
                }
                userIndex = i;
                break;
            }
        }

        if (userText == null || userIndex < 0) return;

        // Remove retry button, last AI bubble(s), and the user bubble
        RemoveRetryButton();

        // Remove everything from userIndex onwards (user bubble + AI response)
        while (ChatPanel.Children.Count > userIndex)
            ChatPanel.Children.RemoveAt(ChatPanel.Children.Count - 1);

        // Sync API history
        _geminiService.RemoveLastExchange();

        // Re-add user bubble and re-ask
        AddChatBubble("You", userText);
        FireAndForget(AskGeminiAsync(userText), "RetryLastMessage");
    }

    private void AnimateToChatMode()
    {
        HideWidgetPanel();
        HideSearchList();
        ExitChatArrow.Visibility = Visibility.Visible;
        ClearChatBtn.Visibility = Visibility.Visible;
        ExportChatBtn.Visibility = Visibility.Visible;
        LoadChatBtn.Visibility = Visibility.Visible;
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
        LoadChatBtn.Visibility = Visibility.Collapsed;
        PinBtn.Visibility = Visibility.Collapsed;

        AnimateOrSnap(ChatScale, ScaleTransform.ScaleYProperty, 0, TimeSpan.FromSeconds(0.25),
            new CubicEase { EasingMode = EasingMode.EaseIn },
            () =>
            {
                ChatScroll.Visibility = Visibility.Collapsed;
                UpdateResumeIndicator();
                if (string.IsNullOrEmpty(InputBox.Text))
                    ShowWidgetPanel();
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
        _currentChatSessionFile = null;
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

        _isFileDialogOpen = true;
        try
        {
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
        finally
        {
            _isFileDialogOpen = false;
        }
    }

    private void ExitChatBtn_Click(object sender, MouseButtonEventArgs e)
    {
        AnimateToSpotlightMode();
        ExitChatArrow.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------------------
    // Chat auto-save / load
    // ---------------------------------------------------------------------------

    private static string ChatSavesFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Compass", "Chats");

    private void AutoSaveChatSession()
    {
        if (!_geminiService.HasHistory) return;

        try
        {
            Directory.CreateDirectory(ChatSavesFolder);

            // Create a new session file on first save, reuse it for subsequent saves
            if (string.IsNullOrEmpty(_currentChatSessionFile))
            {
                var history = _geminiService.GetExportableHistory();
                string firstMsg = history.FirstOrDefault(h => h.role == "You").text ?? "Chat";
                // Truncate and sanitize for filename
                string label = firstMsg.Length > 50 ? firstMsg[..50] : firstMsg;
                string safeName = string.Join("_", label.Split(System.IO.Path.GetInvalidFileNameChars()));
                safeName = safeName.Replace(" ", "-");
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _currentChatSessionFile = System.IO.Path.Combine(ChatSavesFolder, $"{timestamp}_{safeName}.json");
            }

            string json = _geminiService.SerializeHistory();
            File.WriteAllText(_currentChatSessionFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-save chat session");
        }
    }

    private void LoadChatState_Click(object sender, MouseButtonEventArgs e)
    {
        BuildSavedChatsPopup();
        LoadChatPopup.IsOpen = !LoadChatPopup.IsOpen;
    }

    private void BuildSavedChatsPopup()
    {
        SavedChatsPanel.Children.Clear();

        if (!Directory.Exists(ChatSavesFolder))
        {
            SavedChatsPanel.Children.Add(new TextBlock
            {
                Text = "No saved chats",
                Foreground = Resources["TextTertiaryBrush"] as Brush,
                FontSize = 12,
                Margin = new Thickness(10, 8, 10, 8)
            });
            return;
        }

        var files = Directory.GetFiles(ChatSavesFolder, "*.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToArray();

        if (files.Length == 0)
        {
            SavedChatsPanel.Children.Add(new TextBlock
            {
                Text = "No saved chats",
                Foreground = Resources["TextTertiaryBrush"] as Brush,
                FontSize = 12,
                Margin = new Thickness(10, 8, 10, 8)
            });
            return;
        }

        foreach (var file in files)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            string date = File.GetLastWriteTime(file).ToString("MMM d, h:mm tt");

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 12.5,
                Foreground = Resources["TextPrimaryBrush"] as Brush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var dateText = new TextBlock
            {
                Text = date,
                FontSize = 9.5,
                Foreground = Resources["TextTertiaryBrush"] as Brush,
                Margin = new Thickness(0, 1, 0, 0)
            };

            var textStack = new StackPanel();
            textStack.Children.Add(nameText);
            textStack.Children.Add(dateText);

            // Delete button
            var deleteBtn = new TextBlock
            {
                Text = "\u2715",
                FontSize = 12,
                Foreground = Resources["TextTertiaryBrush"] as Brush,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(8, 0, 0, 0)
            };

            var row = new DockPanel();
            DockPanel.SetDock(deleteBtn, Dock.Right);
            row.Children.Add(deleteBtn);
            row.Children.Add(textStack);

            var container = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row
            };

            container.MouseEnter += (s, ev) => container.Background = Resources["HoverBrush"] as Brush ?? Brushes.DarkGray;
            container.MouseLeave += (s, ev) => container.Background = Brushes.Transparent;

            string capturedFile = file;
            string capturedName = name;
            container.MouseLeftButtonUp += (s, ev) =>
            {
                LoadChatFromFile(capturedFile, capturedName);
                LoadChatPopup.IsOpen = false;
                ev.Handled = true;
            };
            deleteBtn.MouseLeftButtonUp += (s, ev) =>
            {
                try { File.Delete(capturedFile); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete saved chat: {File}", capturedFile); }
                BuildSavedChatsPopup(); // rebuild
                ev.Handled = true;
            };

            SavedChatsPanel.Children.Add(container);
        }
    }

    private void LoadChatFromFile(string filePath, string name)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            _geminiService.LoadSerializedHistory(json);

            // Rebuild chat UI from history (with images)
            ChatPanel.Children.Clear();
            var history = _geminiService.GetExportableHistoryWithImages();
            foreach (var (role, text, images) in history)
            {
                string sender = role == "You" ? "You" : "Gemini";
                if (images.Count > 0)
                    AddChatBubbleWithGeneratedImages(sender, text, images);
                else
                    AddChatBubble(sender, text);
            }

            _currentChatSessionFile = filePath;

            if (ChatScroll.Visibility != Visibility.Visible)
                AnimateToChatMode();
        }
        catch (Exception ex)
        {
            AddChatBubble("System", $"Failed to load chat: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // Image helpers
    // ---------------------------------------------------------------------------

    private void SaveGeneratedImage(byte[] data, string mimeType)
    {
        string ext = mimeType switch
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
        _isFileDialogOpen = true;
        try
        {
            if (dlg.ShowDialog() == true)
                File.WriteAllBytes(dlg.FileName, data);
        }
        finally
        {
            _isFileDialogOpen = false;
        }
    }

    private void ShowImageOverlay(BitmapImage image)
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Cursor = Cursors.Hand
        };

        var img = new System.Windows.Controls.Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            MaxWidth = SystemParameters.PrimaryScreenWidth * 0.8,
            MaxHeight = SystemParameters.PrimaryScreenHeight * 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };
        overlay.Children.Add(img);

        // Dismiss on click anywhere
        overlay.MouseLeftButtonUp += (s, e) =>
        {
            var parent = overlay.Parent as Panel;
            parent?.Children.Remove(overlay);
        };

        // Add overlay on top of the main grid
        if (Content is Grid mainGrid)
            mainGrid.Children.Add(overlay);
    }

}
