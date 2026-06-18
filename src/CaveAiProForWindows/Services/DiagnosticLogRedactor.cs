using System.Text.RegularExpressions;

namespace CaveAiProForWindows.Services;

/// <summary>Redacts secrets and PII from diagnostic log lines and JSON before export.</summary>
internal static class DiagnosticLogRedactor
{
    private static readonly Regex EmailPattern =
        new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);

    private static readonly Regex JwtPattern =
        new(@"\beyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]*\b", RegexOptions.Compiled);

    private static readonly Regex FirebaseKeyPattern =
        new(@"\bAIzaSy[A-Za-z0-9_-]{20,}\b", RegexOptions.Compiled);

    private static readonly Regex OpenAiKeyPattern =
        new(@"\bsk-[a-zA-Z0-9]{20,}\b", RegexOptions.Compiled);

    private static readonly Regex BearerPattern =
        new(@"\bBearer\s+[A-Za-z0-9._\-+/=]{10,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WindowsUserPathPattern =
        new(@"(?<![A-Za-z0-9])C:\\Users\\[^\\]+\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RedactLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return line ?? string.Empty;

        var text = line;
        text = EmailPattern.Replace(text, "[REDACTED_EMAIL]");
        text = JwtPattern.Replace(text, "[REDACTED_JWT]");
        text = FirebaseKeyPattern.Replace(text, "[REDACTED_API_KEY]");
        text = OpenAiKeyPattern.Replace(text, "[REDACTED_TOKEN]");
        text = BearerPattern.Replace(text, "Bearer [REDACTED]");
        text = WindowsUserPathPattern.Replace(text, ".../");
        return text;
    }

    public static string RedactFileContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = RedactLine(lines[i].TrimEnd('\r'));
        return string.Join(Environment.NewLine, lines);
    }

    public static string RedactJsonSecrets(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        var text = json;
        text = FirebaseKeyPattern.Replace(text, "[REDACTED_API_KEY]");
        text = OpenAiKeyPattern.Replace(text, "[REDACTED_TOKEN]");
        text = JwtPattern.Replace(text, "[REDACTED_JWT]");
        text = EmailPattern.Replace(text, "[REDACTED_EMAIL]");
        text = BearerPattern.Replace(text, "Bearer [REDACTED]");
        return text;
    }
}
