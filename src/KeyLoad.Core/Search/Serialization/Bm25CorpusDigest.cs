using System.Security.Cryptography;

namespace KeyLoad.Core.Search;

internal static class Bm25CorpusDigest
{
    private const string SourceDigestDomain = "keyload-bm25-corpus-source";
    private static readonly byte[] SourceDigestDomainBytes = System.Text.Encoding.UTF8.GetBytes(SourceDigestDomain);

    internal static string Compute(Bm25CorpusCurrent current, AtomicPartitionPlacementResolution placement,
        ReadExecutionBudget work)
    {
        work.ChargeBytes(Measure(current, placement));
        work.Check();
        return Compute(current, placement);
    }

    internal static string Compute(Bm25CorpusCurrent current, AtomicPartitionPlacementResolution placement)
    {
        var bytes = OrleansSerialization.Serialize(current with
        {
            PayloadDigest = string.Empty
        });
        byte[]? placementBytes = null;
        try
        {
            placementBytes = OrleansSerialization.Serialize(placement);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(SourceDigestDomainBytes);
            hash.AppendData(bytes);
            hash.AppendData(placementBytes);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (placementBytes is not null)
            {
                CryptographicOperations.ZeroMemory(placementBytes);
            }
        }
    }

    internal static string Resource(ResourceDefinition resource, ReadExecutionBudget work)
    {
        work.ChargeBytes(checked(OrleansSerialization.Measure(resource) + SHA256.HashSizeInBytes));
        work.Check();
        return Resource(resource);
    }

    internal static string Resource(ResourceDefinition resource)
    {
        var bytes = OrleansSerialization.Serialize(resource);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static long Measure(Bm25CorpusCurrent current, AtomicPartitionPlacementResolution placement)
        => checked(OrleansSerialization.Measure(current with
        {
            PayloadDigest = string.Empty
        })
            + OrleansSerialization.Measure(placement) + SourceDigestDomainBytes.Length
            + SHA256.HashSizeInBytes);
}
