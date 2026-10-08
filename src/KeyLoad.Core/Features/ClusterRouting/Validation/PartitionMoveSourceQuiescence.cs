using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveSourceQuiescence
{
    private static readonly string[] Families =
    [PartitionRecordFamilies.Lease, PartitionRecordFamilies.SubscriptionWindow,
        PartitionRecordFamilies.BlobState, PartitionRecordFamilies.ProjectionConsumer];

    internal static void Require(IKeyValueView view, PartitionRef partition, DatabaseLimits limits)
    {
        var remaining = limits.MaxScanRecords;
        long bytes = PartitionMoveProtocol.EmptyCount;
        foreach (var family in Families)
        {
            if (remaining <= PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            var scan = view.VisitRange(KeySpace.Partition(family, partition), remaining, (key, value) =>
            {
                bytes = checked(bytes + key.Length + value.Length);
                if (bytes > limits.MaxQueryReadBytes || value.Length > limits.MaxBatchBytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
                if (!Settled(family, value, partition))
                { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.QuiescenceRequired); }
                return true;
            });
            remaining = checked(remaining - scan.Records);
            if (scan.HasMore)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        }
    }

    private static bool Settled(string family, ReadOnlySpan<byte> bytes, PartitionRef partition)
    {
        if (family == PartitionRecordFamilies.BlobState)
        {
            var state = BlobRecordReader.Decode<BlobState>(bytes);
            if (state.Blob.Partition != partition)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            return state.Status != BlobUploadStatus.Active;
        }
        if (family == PartitionRecordFamilies.ProjectionConsumer)
        {
            var state = NativeSerialization.Deserialize<ProjectionConsumerInfo>(bytes);
            if (state.Consumer.Partition != partition || state.Checkpoint < PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            return state.Released;
        }
        return false;
    }
}
