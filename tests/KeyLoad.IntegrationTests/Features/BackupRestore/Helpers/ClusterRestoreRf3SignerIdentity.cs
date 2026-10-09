using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3SignerIdentity
{
    internal static string Fingerprint(string signingKey)
    {
        var original = Convert.FromBase64String(signingKey);
        try
        { return Convert.ToHexStringLower(SHA256.HashData(original)); }
        finally { CryptographicOperations.ZeroMemory(original); }
    }
}
