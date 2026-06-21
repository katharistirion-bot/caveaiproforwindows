using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CaveAiProForWindows.Services.CloudPublish;

public static class PublicLibrarySearchKey
{
    private static readonly Regex CombiningMarks = new(@"\p{M}+", RegexOptions.Compiled);
    private static readonly Regex GreekScript = new(@"[\u0370-\u03FF\u1F00-\u1FFF]", RegexOptions.Compiled);

    private static readonly Dictionary<char, string> GreekLowerMap = new()
    {
        ['\u03B1'] = "a", ['\u03AC'] = "a", ['\u03B2'] = "v", ['\u03B3'] = "g", ['\u03B4'] = "d",
        ['\u03B5'] = "e", ['\u03AD'] = "e", ['\u03B6'] = "z", ['\u03B7'] = "i", ['\u03AE'] = "i",
        ['\u03B8'] = "th", ['\u03B9'] = "i", ['\u03AF'] = "i", ['\u03CA'] = "i", ['\u0390'] = "i",
        ['\u03BA'] = "k", ['\u03BB'] = "l", ['\u03BC'] = "m", ['\u03BD'] = "n", ['\u03BE'] = "x",
        ['\u03BF'] = "o", ['\u03CC'] = "o", ['\u03C0'] = "p", ['\u03C1'] = "r", ['\u03C3'] = "s",
        ['\u03C2'] = "s", ['\u03C4'] = "t", ['\u03C5'] = "y", ['\u03CD'] = "y", ['\u03CB'] = "y",
        ['\u03B0'] = "y", ['\u03C6'] = "f", ['\u03C7'] = "ch", ['\u03C8'] = "ps", ['\u03C9'] = "o",
        ['\u03CE'] = "o",
    };

    public static string FromCaveName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var latin = ToLatinDisplay(raw.Trim());
        var lower = latin.ToLower(CultureInfo.InvariantCulture);
        var normalized = lower.Normalize(NormalizationForm.FormD);
        var stripped = CombiningMarks.Replace(normalized, string.Empty);
        return stripped.Length <= 500 ? stripped : stripped[..500];
    }

    private static string ToLatinDisplay(string text)
    {
        if (!GreekScript.IsMatch(text))
            return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            var lower = char.ToLower(ch, CultureInfo.InvariantCulture);
            if (GreekLowerMap.TryGetValue(lower, out var mapped))
            {
                if (char.IsUpper(ch) && mapped.Length == 1)
                    sb.Append(char.ToUpper(mapped[0], CultureInfo.InvariantCulture));
                else if (char.IsUpper(ch))
                    sb.Append(char.ToUpper(mapped[0], CultureInfo.InvariantCulture)).Append(mapped.AsSpan(1));
                else
                    sb.Append(mapped);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
