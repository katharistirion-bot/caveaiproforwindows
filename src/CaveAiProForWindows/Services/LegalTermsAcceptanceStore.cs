using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Persists acceptance of the in-app legal disclaimer (LocalApplicationData).</summary>
public static class LegalTermsAcceptanceStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    private static string StorePath => Path.Combine(StoreDirectory, "legal_terms_acceptance.json");

    public static bool Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return false;
            var json = File.ReadAllText(StorePath);
            var dto = JsonSerializer.Deserialize<Dto>(json, JsonOpts);
            return dto?.Accepted == true;
        }
        catch
        {
            return false;
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

        public DateTime? AcceptedAtUtc { get; set; }
    }
}
