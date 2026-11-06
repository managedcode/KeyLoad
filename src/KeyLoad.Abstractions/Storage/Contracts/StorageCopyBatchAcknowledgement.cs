using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Confirms one physical copy's applied prefix; only the selected result copy returns full outcomes.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(StorageCopyBatchAcknowledgement.SerializerAlias)]
public sealed record StorageCopyBatchAcknowledgement(
    [property: Orleans.Id(0)] long Position,
    [property: Orleans.Id(1)] ImmutableArray<StorageCopyCommandAcknowledgement> Applied,
    [property: Orleans.Id(2)] StorageBatchResult? Outcome)
{
    internal const string SerializerAlias = "keyload.storage-copy-batch-acknowledgement";

    /// <summary>The primary's persisted effects travel independently of its caller's result payload.</summary>
    [Orleans.Id(3)] public StorageReplicaBatch? Replication { get; init; }
}
