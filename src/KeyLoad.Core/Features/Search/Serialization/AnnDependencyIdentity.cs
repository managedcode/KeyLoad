using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class AnnDependencyIdentity
{
    private const string Domain = "keyload.ann.native-policy.v1";
    private const int DigestBytes = 32;
    private const string InvalidIdentity = "The canonical ANN dependency identity is inconsistent.";
    private const string ScanExceeded = "The canonical ANN dependency scope exceeds its budget.";

    internal static string Capture(DatabaseEngine database, IKeyValueView view, PartitionRef partition,
        ReadExecutionBudget budget, AnnSeedWork work)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Domain));
        var scanned = budget.VisitRange(view, KeySpace.ResourcePrefix(partition.TenantId, partition.DatabaseId),
            database.Limits.MaxScanRecords, (key, value) =>
            {
                work.Charge();
                var resource = NativeSerialization.Deserialize<ResourceDefinition>(value);
                if (!key.SequenceEqual(KeySpace.Resource(partition.TenantId, partition.DatabaseId, resource.Name)))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
                if (resource.TransactionDomainId == partition.TransactionDomainId)
                {
                    Append(hash, key, work);
                    Append(hash, value, work);
                }
                return true;
            });
        if (scanned.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScanExceeded); }
        work.Check();
        Span<byte> digest = stackalloc byte[DigestBytes];
        if (!hash.TryGetHashAndReset(digest, out var written) || written != DigestBytes)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidIdentity); }
        return Convert.ToHexStringLower(digest);
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes, AnnSeedWork work)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        work.Charge(checked(length.Length + bytes.Length));
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
