using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Receiver-local canonical metadata, outside the immutable transferred model-family inventory.</summary>
internal static class PartitionMoveReceiverIssuanceStorage
{
    private const string IssuanceSpace = "pmri";
    private const string QuotaSpace = "pmri-quota";

    private const string IssuanceCountMarker = "count";
    private const string IssuanceBytesMarker = "bytes";

    internal static byte[] Key(PartitionRef partition, Guid moveId, Guid originalId)
        => KeySpace.Partition(IssuanceSpace, partition, moveId, originalId);

    internal static byte[] CountKey(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(QuotaSpace, partition, moveId, IssuanceCountMarker);

    internal static byte[] BytesKey(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(QuotaSpace, partition, moveId, IssuanceBytesMarker);

    internal static PartitionMoveReceiverIssuance? Read(IKeyValueView view, PartitionRef partition,
        Guid moveId, Guid originalId, int maximumBytes)
        => PartitionMoveTargetStorage.Read<PartitionMoveReceiverIssuance>(view,
            Key(partition, moveId, originalId), maximumBytes);

    internal static void Write(IAtomicTransaction transaction, PartitionMoveReceiverIssuance value,
        int maximumBytes, int maximumPhases, long maximumMetadataBytes)
    {
        PartitionMoveParentStorage.ChangeCounter(transaction, CountKey(value.Partition, value.MoveId), PartitionMoveProtocol.SequenceStep, maximumPhases);
        PartitionMoveParentStorage.ChangeCounter(transaction, BytesKey(value.Partition, value.MoveId),
            NativeSerialization.Measure(value), maximumMetadataBytes);
        PartitionMoveTargetStorage.Write(transaction, Key(value.Partition, value.MoveId,
            value.OriginalPhaseCommandId), value, maximumBytes);
    }
}
