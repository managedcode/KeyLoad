using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Confirms one physical copy's applied prefix; only the selected result copy returns full outcomes.</summary>
[GenerateSerializer]
[Alias(StorageCopyBatchAcknowledgement.SerializerAlias)]
public sealed record StorageCopyBatchAcknowledgement(
    [property: Id(0)] long Position,
    [property: Id(1)] ImmutableArray<StorageCopyCommandAcknowledgement> Applied,
    [property: Id(2)] StorageBatchResult? Outcome)
{
    internal const string SerializerAlias = "keyload.storage-copy-batch-acknowledgement";
}
