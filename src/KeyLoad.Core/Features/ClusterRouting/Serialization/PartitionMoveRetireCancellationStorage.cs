using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Bounded receiver-local monotonic barriers share the native issuance metadata quota.</summary>
internal static class PartitionMoveRetireCancellationStorage
{
    private const string CancellationSpace = "pm-retire-cancel";
    private const int OneRecord = 1;

    internal static byte[] Key(PartitionRef partition, Guid moveId, Guid originalId)
        => KeySpace.Partition(CancellationSpace, partition, moveId, originalId);

    internal static PartitionMoveExpiredRetireCancellation? Read(IKeyValueView view,
        PartitionRef partition, Guid moveId, Guid originalId, int maximumBytes)
        => PartitionMoveTargetStorage.Read<PartitionMoveExpiredRetireCancellation>(view,
            Key(partition, moveId, originalId), maximumBytes);

    internal static void Write(IAtomicTransaction transaction, PartitionMoveExpiredRetireCancellation value,
        int maximumBytes, int maximumPhases, long maximumMetadataBytes)
    {
        var phase = value.CancellationPhase;
        PartitionMoveParentStorage.ChangeCounter(transaction,
            PartitionMoveReceiverIssuanceStorage.CountKey(phase.Partition, phase.MoveId), OneRecord, maximumPhases);
        PartitionMoveParentStorage.ChangeCounter(transaction,
            PartitionMoveReceiverIssuanceStorage.BytesKey(phase.Partition, phase.MoveId),
            NativeSerialization.Measure(value), maximumMetadataBytes);
        PartitionMoveTargetStorage.Write(transaction, Key(phase.Partition, phase.MoveId,
            value.OriginalPhaseCommandId), value, maximumBytes);
    }
}
