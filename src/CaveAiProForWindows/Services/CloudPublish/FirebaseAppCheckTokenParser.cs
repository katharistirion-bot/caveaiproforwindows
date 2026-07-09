using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Minimal JWT payload parser for App Check tokens (exp only).</summary>
public static class FirebaseAppCheckTokenParser
{
    public static bool TryParse(string? jwt, out FirebaseAppCheckToken? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(jwt))
            return false;

        var parts = jwt.Trim().Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expEl))
                return false;

            var exp = expEl.ValueKind switch
            {
                JsonValueKind.Number => expEl.GetInt64(),
                JsonValueKind.String when long.TryParse(expEl.GetString(), out var l) => l,
                _ => 0L,
            };
            if (exp <= 0)
                return false;

            token = new FirebaseAppCheckToken
            {
                Raw = jwt.Trim(),
                ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp),
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}