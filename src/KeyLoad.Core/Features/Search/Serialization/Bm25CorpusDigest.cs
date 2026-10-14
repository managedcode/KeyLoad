using System.Security.Cryptography;

namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusDigest
{
    private const string SourceDigestDomain = "keyload-bm25-corpus-source-v1";
    private static readonly byte[] SourceDigestDomainBytes = System.Text.Encoding.UTF8.GetBytes(SourceDigestDomain);

    internal static string Compute(Bm25CorpusCurrentV1 current, AtomicPartitionPlacementResolution placement,
        ReadExecutionBudget work)
    {
        work.ChargeBytes(Measure(current, placement));
        work.Check();
        return Compute(current, placement, work.MaximumResultBytes);
    }

    internal static string Compute(Bm25CorpusCurrentV1 current, AtomicPartitionPlacementResolution placement,
        long maximumBytes)
    {
        if (Measure(current, placement) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
        }
        var bytes = NativeSerialization.Serialize(current with
        {
            PayloadDigest = string.Empty
        });
        byte[]? placementBytes = null;
        try
        {
            placementBytes = NativeSerialization.Serialize(placement);
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
        work.ChargeBytes(checked(NativeSerialization.Measure(resource) + SHA256.HashSizeInBytes));
        work.Check();
        return Resource(resource, work.MaximumResultBytes);
    }

    internal static string Resource(ResourceDefinition resource, long maximumBytes)
    {
        if (checked(NativeSerialization.Measure(resource) + SHA256.HashSizeInBytes) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
        }
        var bytes = NativeSerialization.Serialize(resource);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static long Measure(Bm25CorpusCurrentV1 current, AtomicPartitionPlacementResolution placement)
        => checked(NativeSerialization.Measure(current with
        {
            PayloadDigest = string.Empty
        })
            + NativeSerialization.Measure(placement) + SourceDigestDomainBytes.Length
            + SHA256.HashSizeInBytes);
}
