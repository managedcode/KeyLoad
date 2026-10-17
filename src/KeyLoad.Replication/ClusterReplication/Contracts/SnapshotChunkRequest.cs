namespace KeyLoad.Replication;

/// <summary>Supplies exact bytes at an acknowledged transfer offset.</summary>
/// <param name="LeaderId">Configured voter sending the chunk.</param>
/// <param name="Term">Sender's current leadership term.</param>
/// <param name="TransferId">Identity of the fenced transfer.</param>
/// <param name="Offset">Expected contiguous image byte offset.</param>
/// <param name="Bytes">Read-only chunk bytes owned by this request.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotChunkRequest)]
public sealed record SnapshotChunkRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] Guid TransferId,
    [property: Orleans.Id(3)] long Offset,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> Bytes);
