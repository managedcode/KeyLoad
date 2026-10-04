namespace KeyLoad.Replication;

/// <summary>Describes one verified canonical image and its replica-log cut.</summary>
/// <param name="TransferId">Unique identity fencing concurrent image uploads.</param>
/// <param name="Incarnation">Authority incarnation recorded in the image.</param>
/// <param name="Index">Canonical applied and committed replica position.</param>
/// <param name="Term">Term at the represented log position.</param>
/// <param name="Length">Exact encoded image length.</param>
/// <param name="Sha256">Hexadecimal checksum of the complete image.</param>
/// <param name="FileName">Validated image basename derived from its transfer identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaSnapshot)]
public sealed record ReplicaSnapshot(
    [property: Orleans.Id(0)] Guid TransferId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Index,
    [property: Orleans.Id(3)] long Term,
    [property: Orleans.Id(4)] long Length,
    [property: Orleans.Id(5)] string Sha256,
    [property: Orleans.Id(6)] string FileName);
/// <summary>Starts or resumes a fenced incoming canonical image.</summary>
/// <param name="LeaderId">Configured voter sending the image.</param>
/// <param name="Term">Sender's current leadership term.</param>
/// <param name="Snapshot">Complete expected image and cut description.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotBeginRequest)]
public sealed record SnapshotBeginRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] ReplicaSnapshot Snapshot);
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
/// <summary>Requests complete image verification and canonical installation.</summary>
/// <param name="LeaderId">Configured voter completing the image.</param>
/// <param name="Term">Sender's current leadership term.</param>
/// <param name="TransferId">Identity of the completed transfer.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotCompleteRequest)]
public sealed record SnapshotCompleteRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] Guid TransferId);
/// <summary>Returns durable transfer progress or the verified installed cut.</summary>
/// <param name="Term">Receiver's current term.</param>
/// <param name="Offset">Durably acknowledged image byte prefix.</param>
/// <param name="Installed">Whether the complete image has been installed.</param>
/// <param name="Index">Installed replica position, or zero during upload.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotReply)]
public sealed record SnapshotReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] long Offset,
    [property: Orleans.Id(2)] bool Installed,
    [property: Orleans.Id(3)] long Index);

/// <summary>Transfers bounded verified images while borrowing the node's canonical store and durable log.</summary>
public interface IReplicaSnapshotStore
{
    /// <summary>Gets the published verified checkpoint, or null before checkpointing.</summary>
    ReplicaSnapshot? Current { get; }
    /// <summary>Recovers complete installations and validates the published canonical cut.</summary>
    void Recover();
    /// <summary>Recovers any complete verified installation, then abandons only an incomplete fenced upload.</summary>
    void ResetIncoming();
    /// <summary>Captures and publishes a verified checkpoint at the requested committed cut.</summary>
    /// <param name="index">Exact committed and applied replica position.</param>
    /// <param name="term">Term at that position.</param>
    /// <returns>The published verified image description.</returns>
    ReplicaSnapshot Create(long index, long term);
    /// <summary>Begins or resumes a validated image transfer.</summary>
    /// <param name="snapshot">Expected image authority, length, checksum and cut.</param>
    /// <returns>The durably acknowledged byte prefix.</returns>
    long Begin(ReplicaSnapshot snapshot);
    /// <summary>Durably appends an exact bounded chunk or validates an identical retry.</summary>
    /// <param name="transferId">Identity of the active transfer.</param>
    /// <param name="offset">Expected contiguous image byte offset.</param>
    /// <param name="bytes">Borrowed bytes consumed synchronously without retention.</param>
    /// <returns>The durably acknowledged byte prefix.</returns>
    long Append(Guid transferId, long offset, ReadOnlySpan<byte> bytes);
    /// <summary>Verifies and installs one complete transfer before publishing its cut.</summary>
    /// <param name="transferId">Identity of the completed transfer.</param>
    /// <returns>The verified installed image description.</returns>
    ReplicaSnapshot Complete(Guid transferId);
    /// <summary>Copies a bounded chunk from the published verified image.</summary>
    /// <param name="transferId">Identity of the published image.</param>
    /// <param name="offset">Image byte offset to copy.</param>
    /// <param name="maxBytes">Maximum chunk size within configured limits.</param>
    /// <returns>Owned image bytes for the outgoing request.</returns>
    byte[] ReadChunk(Guid transferId, long offset, int maxBytes);
}
