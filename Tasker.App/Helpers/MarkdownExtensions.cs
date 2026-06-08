using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Tasker_App.Helpers;

/// <summary>
/// Minimal Markdown-to-Inlines renderer exposed as an attached property, so assistant replies show
/// **bold**, *italic*, `code`, bullet lists and line breaks instead of raw markup. Deliberately
/// lightweight (no external dependency) and safe for untrusted text.
/// </summary>
public static class MarkdownExtensions
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.RegisterAttached(
            "MarkdownText", typeof(string), typeof(MarkdownExtensions),
            new PropertyMetadata(null, OnMarkdownTextChanged));

    public static string GetMarkdownText(DependencyObject obj) => (string)obj.GetValue(MarkdownTextProperty);
    public static void SetMarkdownText(DependencyObject obj, string value) => obj.SetValue(MarkdownTextProperty, value);

    private static readonly Regex InlineRegex = new(
        @"(\*\*(?<b>.+?)\*\*)|(`(?<c>[^`]+?)`)|(\*(?<i>.+?)\*)|(_(?<u>.+?)_)",
        RegexOptions.Compiled);

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();

        var text = (e.NewValue as string ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) tb.Inlines.Add(new LineBreak());
            AppendLine(tb, lines[i]);
        }
    }

    private static void AppendLine(TextBlock tb, string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            line = new string(' ', line.Length - trimmed.Length) + "\u2022 " + trimmed[2..];

        var pos = 0;
        foreach (Match m in InlineRegex.Matches(line))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run { Text = line[pos..m.Index] });

            if (m.Groups["b"].Success)
                tb.Inlines.Add(new Bold { Inlines = { new Run { Text = m.Groups["b"].Value } } });
            else if (m.Groups["i"].Success)
                tb.Inlines.Add(new Italic { Inlines = { new Run { Text = m.Groups["i"].Value } } });
            else if (m.Groups["u"].Success)
                tb.Inlines.Add(new Italic { Inlines = { new Run { Text = m.Groups["u"].Value } } });
            else if (m.Groups["c"].Success)
                tb.Inlines.Add(new Run { Text = m.Groups["c"].Value, FontFamily = new FontFamily("Consolas") });

            pos = m.Index + m.Length;
        }

        if (pos < line.Length) tb.Inlines.Add(new Run { Text = line[pos..] });
    }
}
