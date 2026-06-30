namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Rule-safe owner sync updates for <c>published_caves</c> — mirrors
/// <c>publishedCaveOwnerSyncUpdateValid</c> in firebase/firestore.rules
/// and Android <c>PublishedCaveFirestorePayload.buildOwnerSyncUpdateMap</c>.
/// </summary>
public static class PublishedCaveSyncPayload
{
    public const int MaxImageUrls = 96;
    public const int MaxCartographyUrls = 24;
    public const int MaxDescriptionChars = 20_000;
    public const int MaxCaveNameChars = 500;
    public const int UrlMaxLen = 3000;
    public const int SurveyReportSummaryMaxChars = 24_000;
    public const int AccessSeasonNoteMaxChars = 500;

    /// <summary>Keys allowed in <c>request.resource.data.diff(resource.data).affectedKeys()</c> for owner sync.</summary>
    public static IReadOnlySet<string> SyncUpdateAllowedKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "caveName",
        "caveNameSearchKey",
        "description",
        "depth",
        "length",
        "imageUrls",
        "cartographyImageUrls",
        "surveyJsonUrl",
        "surfaceLidarUrl",
        "surveyReportSummary",
        "surveyReportNarrativeUrl",
        "entranceMagneticHintsJson",
        "entranceMagneticHintsUrl",
        "lastSyncedAtMs",
        "accessSeasonNote",
    };

    public sealed class OwnerSyncInput
    {
        public required string CaveName { get; init; }

        public required string Description { get; init; }

        public required double Depth { get; init; }

        public required double Length { get; init; }

        public required IReadOnlyList<string> MergedImageUrls { get; init; }

        public IReadOnlyList<string>? MergedCartographyImageUrls { get; init; }

        public string? SurveyJsonUrl { get; init; }

        public string? SurfaceLidarUrl { get; init; }

        public string? SurveyReportSummary { get; init; }

        public string? SurveyReportNarrativeUrl { get; init; }

        public string? EntranceMagneticHintsJson { get; init; }

        public string? EntranceMagneticHintsUrl { get; init; }

        public string? AccessSeasonNote { get; init; }

        public long LastSyncedAtMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static IReadOnlyList<KeyValuePair<string, object?>> BuildOwnerSyncUpdatePairs(OwnerSyncInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var caveName = TrimMax(input.CaveName, MaxCaveNameChars);
        var pairs = new List<KeyValuePair<string, object?>>
        {
            new("caveName", caveName),
            new("caveNameSearchKey", PublicLibrarySearchKey.FromCaveName(caveName)),
            new("description", TrimMax(input.Description, MaxDescriptionChars)),
            new("depth", SanitizeNumber(input.Depth)),
            new("length", SanitizeNumber(input.Length)),
            new("imageUrls", NormalizeUrls(input.MergedImageUrls, MaxImageUrls)),
            new("lastSyncedAtMs", input.LastSyncedAtMs),
        };

        if (input.MergedCartographyImageUrls is { Count: > 0 } carto)
            pairs.Add(new("cartographyImageUrls", NormalizeUrls(carto, MaxCartographyUrls)));

        AddOptionalString(pairs, "surveyJsonUrl", input.SurveyJsonUrl, UrlMaxLen);
        AddOptionalString(pairs, "surfaceLidarUrl", input.SurfaceLidarUrl, UrlMaxLen);
        AddOptionalString(pairs, "surveyReportSummary", input.SurveyReportSummary, SurveyReportSummaryMaxChars);
        AddOptionalString(pairs, "surveyReportNarrativeUrl", input.SurveyReportNarrativeUrl, UrlMaxLen);
        AddOptionalString(pairs, "entranceMagneticHintsJson", input.EntranceMagneticHintsJson, 32_000);
        AddOptionalString(pairs, "entranceMagneticHintsUrl", input.EntranceMagneticHintsUrl, UrlMaxLen);
        AddOptionalString(pairs, "accessSeasonNote", input.AccessSeasonNote, AccessSeasonNoteMaxChars);

        AssertKeysSubsetAllowed(pairs.Select(p => p.Key));
        return pairs;
    }

    public static void AssertKeysSubsetAllowed(IEnumerable<string> keys)
    {
        var illegal = keys.Where(k => !SyncUpdateAllowedKeys.Contains(k)).ToList();
        if (illegal.Count > 0)
        {
            throw new InvalidOperationException(
                $"published_caves sync update contains keys not in firestore.rules: {string.Join(", ", illegal)}");
        }
    }

    private static void AddOptionalString(
        List<KeyValuePair<string, object?>> pairs,
        string key,
        string? value,
        int maxLen)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;
        pairs.Add(new KeyValuePair<string, object?>(key, TrimMax(trimmed, maxLen)));
    }

    private static string[] NormalizeUrls(IReadOnlyList<string> urls, int maxCount) =>
        urls
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(maxCount)
            .ToArray();

    private static double SanitizeNumber(double value) =>
        double.IsFinite(value) ? value : 0;

    private static string TrimMax(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
