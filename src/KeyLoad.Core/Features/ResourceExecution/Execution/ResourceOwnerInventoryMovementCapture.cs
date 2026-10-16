using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMovementCapture
{
    internal static void Capture(IAtomicTransaction transaction, PartitionMoveSourceFenceRecord fence,
        Guid sourceIncarnation, DatabaseLimits limits)
    {
        ResourceOwnerInventoryCatalog.RequireSettledImports(transaction);
        var origin = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        if (origin?.StoreIncarnation != sourceIncarnation)
        {
            origin = null;
        }
        var summary = Summarize(transaction, fence.Partition, origin?.Proven == true, limits);
        var proof = new ResourceOwnerInventoryMovementProofV1(ResourceOwnerInventoryMovementProtocol.Version,
            fence, origin, summary.Count, summary.Digest);
        ResourceOwnerInventoryMovementRows.Write(transaction,
            ResourceOwnerInventoryMovementRows.ProofKey(fence.Partition), proof, limits);
    }

    private static (long Count, string Digest) Summarize(IKeyValueView view, PartitionRef partition,
        bool proven, DatabaseLimits limits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count = ResourceOwnerInventoryMovementProtocol.First;
        long bytes = ResourceOwnerInventoryMovementProtocol.First;
        var prefix = KeySpace.Partition(PartitionRecordFamilies.ResourceOwnerInventoryMember, partition);
        var range = view.VisitRange(prefix, limits.MaxScanRecords, (key, value) =>
        {
            var entry = ResourceOwnerInventoryCatalog.ReadPartitionEntry(key, value, partition, limits);
            if (proven && !entry.Origin.Proven)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
            }
            PartitionMoveImageDigest.Append(hash, key);
            PartitionMoveImageDigest.Append(hash, value);
            count = checked(count + ResourceOwnerInventoryMovementProtocol.Step);
            return true;
        }, observer: observed =>
        {
            if (observed > Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes) - bytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryMovementProtocol.Capacity);
            }
            bytes += observed;
        });
        if (range.HasMore || range.StoppedByVisitor)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryMovementProtocol.Capacity);
        }
        return (count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
