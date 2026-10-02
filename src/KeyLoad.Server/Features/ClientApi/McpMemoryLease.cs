namespace KeyLoad.Server;

/// <summary>Owns one retained-memory reservation until its idempotent release.</summary>
/// <param name="owner">The budget that serializes all changes to this reservation.</param>
/// <param name="lane">The independent memory pool charged by this reservation.</param>
/// <param name="retainedBytes">The initially reserved retained-byte count.</param>
internal sealed class McpMemoryLease(McpMemoryBudget owner, McpMemoryLane lane, long retainedBytes) : IDisposable
{
    /// <summary>Gets the pool charged by this reservation.</summary>
    internal McpMemoryLane Lane { get; } = lane;

    /// <summary>Gets or sets retained bytes while the owning budget lock is held.</summary>
    internal long RetainedBytes { get; set; } = retainedBytes;

    /// <summary>Gets or sets whether release completed while the owning budget lock is held.</summary>
    internal bool Disposed { get; set; }

    /// <summary>Increases retained capacity atomically without releasing the existing reservation on failure.</summary>
    /// <param name="totalBytes">The new total retained-byte count, including existing capacity.</param>
    /// <param name="cancellationToken">Cancels growth before its reservation is committed.</param>
    /// <exception cref="ArgumentOutOfRangeException">The requested total is negative.</exception>
    /// <exception cref="InvalidOperationException">The requested total would shrink the reservation.</exception>
    /// <exception cref="ObjectDisposedException">The reservation was already released.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before growth.</exception>
    /// <exception cref="KeyLoadException">The lane has insufficient retained-memory capacity.</exception>
    public void GrowTo(long totalBytes, CancellationToken cancellationToken = default)
        => owner.Grow(this, totalBytes, cancellationToken);

    /// <summary>Releases the current retained capacity exactly once, including successful growth.</summary>
    public void Dispose() => owner.Release(this);
}
