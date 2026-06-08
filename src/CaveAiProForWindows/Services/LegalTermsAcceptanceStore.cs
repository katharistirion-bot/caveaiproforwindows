using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Services.Legal;

namespace CaveAiProForWindows.Services;

/// <summary>Persists acceptance of the in-app legal disclaimer (LocalApplicationData).</summary>
public static class LegalTermsAcceptanceStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private const string StoreDirEnv = "CAVEAI_LEGAL_TERMS_STORE_DIR";

    private static string StoreDirectory =>
        Environment.GetEnvironmentVariable(StoreDirEnv)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    internal static string StorePathForTests => Path.Combine(StoreDirectory, "legal_terms_acceptance.json");

    private static string StorePath => StorePathForTests;

    /// <summary>True when the user accepted the current <see cref="LegalTexts.DocumentVersion"/>.</summary>
    public static bool Load()
    {
        var state = LoadState();
        return state.IsAcceptedForCurrentDocument;
    }

    public static LegalTermsAcceptanceState LoadState()
    {
        try
        {
            if (!File.Exists(StorePath))
                return LegalTermsAcceptanceState.NotAccepted;

            var json = File.ReadAllText(StorePath);
            var dto = JsonSerializer.Deserialize<Dto>(json, JsonOpts);
            if (dto?.Accepted != true)
                return LegalTermsAcceptanceState.NotAccepted;

            return new LegalTermsAcceptanceState(
                Accepted: true,
                AcceptedDocumentVersion: dto.AcceptedDocumentVersion,
                AcceptedAtUtc: dto.AcceptedAtUtc);
        }
        catch
        {
            return LegalTermsAcceptanceState.NotAccepted;
        }
    }

    public static void Save(bool accepted)
    {
        try
        {
            Directory.CreateDirectory(StoreDirectory);
            var dto = new Dto
            {
                Accepted = accepted,
                AcceptedDocumentVersion = accepted ? LegalTexts.DocumentVersion : null,
                AcceptedAtUtc = accepted ? DateTime.UtcNow : null,
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch
        {
            // non-fatal
        }
    }

    private sealed class Dto
    {
        public bool Accepted { get; set; }

        public string? AcceptedDocumentVersion { get; set; }

        public DateTime? AcceptedAtUtc { get; set; }
    }
}

/// <summary>Stored legal acceptance snapshot.</summary>
public readonly record struct LegalTermsAcceptanceState(
    bool Accepted,
    string? AcceptedDocumentVersion,
    DateTime? AcceptedAtUtc)
{
    public static LegalTermsAcceptanceState NotAccepted => new(false, null, null);

    public bool IsAcceptedForCurrentDocument =>
        Accepted &&
        string.Equals(
            AcceptedDocumentVersion?.Trim(),
            LegalTexts.DocumentVersion,
            StringComparison.Ordinal);
}
