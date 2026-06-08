using System.Security.Cryptography;
using System.Text;

namespace CaveAiProForWindows.Services.Secrets;

/// <summary>Protects short secrets with Windows DPAPI (CurrentUser scope).</summary>
internal static class UserScopedDpapiProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("CaveAiProForWindows.UserScopedSecret.v1");

    public static byte[] ProtectUtf8(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            Entropy,
            DataProtectionScope.CurrentUser);
    }

    public static string? UnprotectUtf8(byte[] protectedBytes)
    {
        if (protectedBytes == null || protectedBytes.Length == 0)
            return null;

        var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
