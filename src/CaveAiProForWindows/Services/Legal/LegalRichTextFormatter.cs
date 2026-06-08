using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CaveAiProForWindows.Services.Legal;

/// <summary>Builds a read-only FlowDocument for the LEGAL tab from plain-text legal copy.</summary>
public static class LegalRichTextFormatter
{
    public static void ApplyPlainText(RichTextBox target, string fullText, Brush? foreground = null)
    {
        var fg = foreground ?? target.Foreground;
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(4),
            TextAlignment = TextAlignment.Left,
            Foreground = fg,
        };

        foreach (var raw in fullText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6) });
                continue;
            }

            var para = new Paragraph(new Run(line))
            {
                FontSize = 13,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 4),
            };

            if (IsEmphasisLine(line))
                para.FontWeight = FontWeights.SemiBold;

            if (line.StartsWith("LEGAL DISCLAIMER", StringComparison.Ordinal) ||
                line.StartsWith("©", StringComparison.Ordinal))
                para.FontWeight = FontWeights.Bold;

            doc.Blocks.Add(para);
        }

        target.Document = doc;
    }

    private static bool IsEmphasisLine(string line) =>
        line.StartsWith("IMPORTANT:", StringComparison.Ordinal) ||
        line.StartsWith("WARNING:", StringComparison.Ordinal) ||
        line.StartsWith("Document version:", StringComparison.Ordinal) ||
        line.StartsWith("Publisher", StringComparison.Ordinal);
}
