namespace KeyLoad.Replication;

/// <summary>Transfers bounded verified images while borrowing the node's canonical store and durable log.</summary>
public interface IReplicaSnapshotStore
{
    /// <summary>Gets the published verified checkpoint, or null before checkpointing.</summary>
    ReplicaSnapshot? Current
    {
        get;
    }
    /// <summary>Reverifies the published recovery image before reclaiming one actual replica-prefix batch.</summary>
    /// <param name="cancellationToken">Original node-owner cancellation.</param>
    /// <returns>The actual number of reclaimed entry keys.</returns>
    int ReclaimCheckpointPrefix(CancellationToken cancellationToken);
    /// <summary>Recovers complete installations and validates the published canonical cut.</summary>
    void Recover();
    /// <summary>Recovers complete installations with the original native owner cancellation.</summary>
    /// <param name="cancellationToken">Original owner or incoming operation token.</param>
    void Recover(CancellationToken cancellationToken);
    /// <summary>Recovers any complete verified installation, then abandons only an incomplete fenced upload.</summary>
    void ResetIncoming();
    /// <summary>Recovers before abandoning an incomplete upload under the original operation token.</summary>
    /// <param name="cancellationToken">Original incoming operation token.</param>
    void ResetIncoming(CancellationToken cancellationToken);
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
    /// <summary>Installs the verified image with its mandatory live guard under original ownership.</summary>
    /// <param name="transferId">Identity of the completed transfer.</param>
    /// <param name="cancellationToken">Original incoming operation token.</param>
    /// <returns>The verified installed image description.</returns>
    ReplicaSnapshot Complete(Guid transferId, CancellationToken cancellationToken);
    /// <summary>Copies a bounded chunk from the published verified image.</summary>
    /// <param name="transferId">Identity of the published image.</param>
    /// <param name="offset">Image byte offset to copy.</param>
    /// <param name="maxBytes">Maximum chunk size within configured limits.</param>
    /// <returns>Owned image bytes for the outgoing request.</returns>
    byte[] ReadChunk(Guid transferId, long offset, int maxBytes);
}
