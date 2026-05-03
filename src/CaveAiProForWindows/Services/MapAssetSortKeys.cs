using System.Globalization;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Stable ordering for map asset rows and underlay candidates so numeric indices sort in survey order (2 before 10).
/// </summary>
public static class MapAssetSortKeys
{
    /// <summary>Brackets like [12] become [00012] so lexicographic sort matches numeric order.</summary>
    public static string OrdinalBracketKey(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return "";
        return Regex.Replace(
            category,
            @"\[(\d+)\]",
            m => "[" + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("D5", CultureInfo.InvariantCulture) + "]",
            RegexOptions.CultureInvariant);
    }

    public static int CompareRows(MapAssetRow a, MapAssetRow b)
    {
        var c = string.Compare(a.ProjectName, b.ProjectName, StringComparison.OrdinalIgnoreCase);
        if (c != 0)
            return c;
        c = string.Compare(OrdinalBracketKey(a.Category), OrdinalBracketKey(b.Category), StringComparison.OrdinalIgnoreCase);
        if (c != 0)
            return c;
        return string.Compare(a.UriOrPath, b.UriOrPath, StringComparison.OrdinalIgnoreCase);
    }
}
