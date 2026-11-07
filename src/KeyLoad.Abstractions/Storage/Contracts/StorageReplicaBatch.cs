using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>The exact effects decided by one partition's primary, retained atomically with that decision.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(StorageReplicaBatch.SerializerAlias)]
public sealed record StorageReplicaBatch(
    [property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] Guid BatchId,
    [property: Orleans.Id(2)] PartitionRef? Partition,
    [property: Orleans.Id(3)] long PreviousPosition,
    [property: Orleans.Id(4)] long Position,
    [property: Orleans.Id(5)] ImmutableArray<StorageReplicaEffect> Changes,
    [property: Orleans.Id(6)] ImmutableArray<StorageCopyCommandAcknowledgement> Applied)
{
    internal const string SerializerAlias = "keyload.storage-replica-batch";

    /// <summary>Other command prefixes observed by the primary's native decision snapshot.</summary>
    [Orleans.Id(7)] public ImmutableArray<StoragePartitionPosition> Dependencies { get; init; } = [];
}
