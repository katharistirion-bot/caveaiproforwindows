using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Minimal JWT payload parser for Firebase ID tokens (no signature verification — token was issued by Google to the embedded web session).</summary>
public static class FirebaseIdTokenParser
{
    public static bool TryParse(string? jwt, out FirebaseIdToken? token)
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

            long iat = 0;
            if (root.TryGetProperty("iat", out var iatEl))
            {
                iat = iatEl.ValueKind switch
                {
                    JsonValueKind.Number => iatEl.GetInt64(),
                    JsonValueKind.String when long.TryParse(iatEl.GetString(), out var l) => l,
                    _ => 0L,
                };
            }

            token = new FirebaseIdToken
            {
                Raw = jwt.Trim(),
                Subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() : null,
                Email = root.TryGetProperty("email", out var em) ? em.GetString() : null,
                ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp),
                IssuedAtUtc = iat > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(iat)
                    : DateTimeOffset.UtcNow,
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
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}
