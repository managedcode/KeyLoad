using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortIdentity
{
    internal static void Release(string ownerId, byte[] secret, byte[]? canonicalKey)
    {
        RequestCqrsCohortOwners.Release(ownerId);
        CryptographicOperations.ZeroMemory(secret);
        if (canonicalKey is not null)
        {
            CryptographicOperations.ZeroMemory(canonicalKey);
        }
    }
}
