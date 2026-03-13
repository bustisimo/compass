using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Compass.Services;

/// <summary>
/// Renders markdown text into WPF UIElements for chat bubbles.
/// </summary>
public static class MarkdownRenderer
{
    public static List<UIElement> Render(string markdown, ResourceDictionary resources)
    {
        var elements = new List<UIElement>();
        var segments = SplitCodeBlocks(markdown);

        foreach (var (content, isCode, language) in segments)
        {
            if (isCode)
            {
                elements.Add(BuildCodeBlock(content.Trim(), language, resources));
            }
            else if (!string.IsNullOrWhiteSpace(content))
            {
                // Process line by line for block-level elements
                var lines = content.Split('\n');
                var paragraphLines = new List<string>();

                foreach (var rawLine in lines)
                {
                    var line = rawLine;

                    // Headers
                    if (line.StartsWith("### "))
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        elements.Add(CreateHeaderBlock(line[4..], 15, resources));
                        continue;
                    }
                    if (line.StartsWith("## "))
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        elements.Add(CreateHeaderBlock(line[3..], 16, resources));
                        continue;
                    }
                    if (line.StartsWith("# "))
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        elements.Add(CreateHeaderBlock(line[2..], 18, resources));
                        continue;
                    }

                    // Blockquote
                    if (line.StartsWith("> "))
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        elements.Add(CreateBlockquote(line[2..], resources));
                        continue;
                    }

                    // Bullet list
                    if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        int indent = line.Length - line.TrimStart().Length;
                        string text = line.TrimStart()[2..];
                        elements.Add(CreateBulletItem(text, indent, resources));
                        continue;
                    }

                    // Numbered list
                    var numberedMatch = Regex.Match(line.TrimStart(), @"^(\d+)\.\s+(.*)$");
                    if (numberedMatch.Success)
                    {
                        FlushParagraph(paragraphLines, elements, resources);
                        int indent = line.Length - line.TrimStart().Length;
                        string text = numberedMatch.Groups[2].Value;
                        string number = numberedMatch.Groups[1].Value;
                        elements.Add(CreateNumberedItem(text, number, indent, resources));
                        continue;
                    }

                    paragraphLines.Add(line);
                }

                FlushParagraph(paragraphLines, elements, resources);
            }
        }

        return elements;
    }

    private static void FlushParagraph(List<string> lines, List<UIElement> elements, ResourceDictionary resources)
    {
        var text = string.Join("\n", lines).Trim();
        lines.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 14,
            Foreground = resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
            Margin = new Thickness(0, 2, 0, 2)
        };

        AddFormattedInlines(tb.Inlines, text, resources);
        elements.Add(tb);
    }

    private static TextBlock CreateHeaderBlock(string text, double fontSize, ResourceDictionary resources)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
            Margin = new Thickness(0, 6, 0, 4)
        };
        AddFormattedInlines(tb.Inlines, text, resources);
        return tb;
    }

    private static Border CreateBlockquote(string text, ResourceDictionary resources)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 14,
            Foreground = resources["TextSecondaryBrush"] as Brush ?? Brushes.LightGray,
            Margin = new Thickness(0)
        };
        AddFormattedInlines(tb.Inlines, text, resources);

        return new Border
        {
            BorderBrush = resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 4, 4, 4),
            Margin = new Thickness(0, 2, 0, 2),
            Child = tb
        };
    }

    private static TextBlock CreateBulletItem(string text, int indent, ResourceDictionary resources)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 14,
            Foreground = resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
            Margin = new Thickness(indent * 4 + 8, 1, 0, 1)
        };
        tb.Inlines.Add(new Run("\u2022  ") { Foreground = resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue });
        AddFormattedInlines(tb.Inlines, text, resources);
        return tb;
    }

    private static TextBlock CreateNumberedItem(string text, string number, int indent, ResourceDictionary resources)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 14,
            Foreground = resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
            Margin = new Thickness(indent * 4 + 8, 1, 0, 1)
        };
        tb.Inlines.Add(new Run($"{number}. ") { Foreground = resources["AccentBrush"] as Brush ?? Brushes.CornflowerBlue, FontWeight = FontWeights.SemiBold });
        AddFormattedInlines(tb.Inlines, text, resources);
        return tb;
    }

    /// <summary>
    /// Parses inline formatting: **bold**, *italic*, `inline code`
    /// </summary>
    private static void AddFormattedInlines(InlineCollection inlines, string text, ResourceDictionary resources)
    {
        // Pattern matches: **bold**, *italic*, `code`
        var regex = new Regex(@"(\*\*(.+?)\*\*|\*(.+?)\*|`([^`]+)`)");
        int lastIndex = 0;

        foreach (Match match in regex.Matches(text))
        {
            // Add text before match
            if (match.Index > lastIndex)
                inlines.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups[2].Success) // **bold**
            {
                inlines.Add(new Run(match.Groups[2].Value) { FontWeight = FontWeights.Bold });
            }
            else if (match.Groups[3].Success) // *italic*
            {
                inlines.Add(new Run(match.Groups[3].Value) { FontStyle = FontStyles.Italic });
            }
            else if (match.Groups[4].Success) // `code`
            {
                var codeBg = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                inlines.Add(new Run(match.Groups[4].Value)
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                    FontSize = 12.5,
                    Background = codeBg
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
            inlines.Add(new Run(text[lastIndex..]));
    }

    /// <summary>
    /// Builds a styled code block with language label and copy button.
    /// </summary>
    private static UIElement BuildCodeBlock(string code, string? language, ResourceDictionary resources)
    {
        bool isDark = true;
        if (resources["SurfaceBrush"] is SolidColorBrush surfaceBrush)
        {
            var c = surfaceBrush.Color;
            isDark = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 < 0.5;
        }

        var outerBorder = new Border
        {
            Background = new SolidColorBrush(isDark ? Color.FromRgb(0x0D, 0x0D, 0x0D) : Color.FromRgb(0xF0, 0xF0, 0xF0)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 0, 4),
            ClipToBounds = true
        };

        var dock = new DockPanel();

        // Header with language label + copy button
        var headerGrid = new Grid
        {
            Background = new SolidColorBrush(isDark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x20, 0x00, 0x00, 0x00)),
            Margin = new Thickness(0)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var langLabel = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "code" : language.Trim(),
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            Foreground = resources["TextTertiaryBrush"] as Brush ?? Brushes.Gray,
            Margin = new Thickness(10, 5, 0, 5),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langLabel, 0);
        headerGrid.Children.Add(langLabel);

        var copyBtn = new Border
        {
            Background = new SolidColorBrush(isDark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x20, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 3, 8, 3),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var copyText = new TextBlock
        {
            Text = "Copy",
            FontSize = 11,
            Foreground = resources["TextSecondaryBrush"] as Brush ?? Brushes.LightGray
        };
        copyBtn.Child = copyText;

        string capturedCode = code;
        copyBtn.MouseLeftButtonUp += (s, e) =>
        {
            try
            {
                Clipboard.SetText(capturedCode);
                copyText.Text = "Copied!";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { copyText.Text = "Copy"; timer.Stop(); };
                timer.Start();
            }
            catch { }
            e.Handled = true;
        };
        copyBtn.MouseEnter += (s, e) => copyBtn.Background = new SolidColorBrush(isDark ? Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
        copyBtn.MouseLeave += (s, e) => copyBtn.Background = new SolidColorBrush(isDark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x20, 0x00, 0x00, 0x00));

        Grid.SetColumn(copyBtn, 1);
        headerGrid.Children.Add(copyBtn);

        DockPanel.SetDock(headerGrid, Dock.Top);
        dock.Children.Add(headerGrid);

        // Code content
        var codeText = new TextBox
        {
            Text = code,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 12.5,
            Foreground = resources["TextPrimaryBrush"] as Brush ?? Brushes.White,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 10, 8),
            Cursor = Cursors.IBeam,
            CaretBrush = resources["TextPrimaryBrush"] as Brush ?? Brushes.White
        };
        dock.Children.Add(codeText);

        outerBorder.Child = dock;
        return outerBorder;
    }

    /// <summary>
    /// Splits text into alternating segments: (text, false, null) and (code, true, language).
    /// </summary>
    public static List<(string content, bool isCode, string? language)> SplitCodeBlocks(string text)
    {
        var result = new List<(string, bool, string?)>();
        int idx = 0;
        while (idx < text.Length)
        {
            int start = text.IndexOf("```", idx);
            if (start < 0)
            {
                result.Add((text[idx..], false, null));
                break;
            }

            if (start > idx)
                result.Add((text[idx..start], false, null));

            // Extract language tag from the opening ``` line
            int langStart = start + 3;
            int lineEnd = text.IndexOf('\n', langStart);
            string? language = null;

            if (lineEnd < 0)
            {
                // No newline after ``` — treat rest as code
                string possibleLang = text[langStart..].Trim();
                if (possibleLang.Length > 0 && possibleLang.Length < 20)
                    language = possibleLang;
                result.Add(("", true, language));
                break;
            }

            string langTag = text[langStart..lineEnd].Trim();
            if (langTag.Length > 0 && langTag.Length < 20 && !langTag.Contains(' '))
                language = langTag;

            int codeStart = lineEnd + 1;
            int end = text.IndexOf("```", codeStart);
            if (end < 0)
            {
                result.Add((text[codeStart..], true, language));
                break;
            }

            result.Add((text[codeStart..end], true, language));
            idx = end + 3;
            if (idx < text.Length && text[idx] == '\n') idx++;
        }
        return result;
    }
}
