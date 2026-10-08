using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMovePublishedPlacementStorage
{
    internal const string Space = "partition-move-published-placement-v1";

    internal static byte[] Key(PartitionRef partition) => KeySpace.Partition(Space, partition);

    internal static PartitionMovePublishedPlacement? Read(IKeyValueView view, PartitionRef partition)
        => AtomicPartitionPlacementSerialization.Read<PartitionMovePublishedPlacement>(view, Key(partition));

    internal static void Write(IAtomicTransaction transaction, PartitionMovePublishedPlacement record)
        => transaction.Put(Key(record.Placement.Partition),
            AtomicPartitionPlacementSerialization.SerializeBounded(record));
}
