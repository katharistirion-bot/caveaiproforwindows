using System.Runtime.InteropServices;
using System.Text;

namespace CaveAiProForWindows.Services.Secrets;

/// <summary>
/// Stores secrets in the Windows Credential Manager (never written to ui-settings.json).
/// </summary>
public static class WindowsCredentialSecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public static bool TryRead(string targetName, out string? secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        if (!CredRead(targetName, CredTypeGeneric, 0, out var credPtr) || credPtr == IntPtr.Zero)
            return false;

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return false;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            secret = Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrEmpty(secret);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static bool TryWrite(string targetName, string secret, string? comment = null)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        var blob = Encoding.UTF8.GetBytes(secret ?? "");
        var blobPtr = Marshal.AllocCoTaskMem(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                Comment = comment ?? "",
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName,
            };
            return CredWrite(ref cred, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(blobPtr);
        }
    }

    public static bool TryDelete(string targetName) =>
        !string.IsNullOrWhiteSpace(targetName) && CredDelete(targetName, CredTypeGeneric, 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint reserved, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint reserved);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);
}
