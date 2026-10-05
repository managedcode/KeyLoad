namespace KeyLoad.Server;

/// <summary>Bounds MCP retained memory using independent byte pools without execution or identity quotas.</summary>
internal sealed class McpMemoryBudget
{
    private const string CapacityDetail = "The MCP retained-memory budget is exhausted.";
    private const string ShrinkDetail = "An MCP retained-memory reservation cannot shrink.";

    private readonly Lock gate = new();
    private readonly long[] limits;
    private readonly long[] retained;

    /// <summary>Creates independent memory pools for data, control and unclassified ingress.</summary>
    /// <param name="dataBytes">The positive retained-memory capacity for data operations.</param>
    /// <param name="controlBytes">The positive retained-memory capacity reserved for control operations.</param>
    /// <param name="ingressBytes">The positive retained-memory capacity for unclassified protocol parsing.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any pool capacity is zero or negative.</exception>
    public McpMemoryBudget(long dataBytes, long controlBytes, long ingressBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controlBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ingressBytes);
        limits = [dataBytes, controlBytes, ingressBytes];
        retained = new long[limits.Length];
    }

    /// <summary>Immediately reserves retained bytes in one lane or fails without changing accounting.</summary>
    /// <param name="lane">The independent memory pool to charge.</param>
    /// <param name="retainedBytes">The nonnegative retained-byte count to reserve.</param>
    /// <param name="cancellationToken">Cancels admission before the reservation is committed.</param>
    /// <returns>An owned reservation that must remain alive while its memory is retained.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The lane is undefined or the byte count is negative.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before reservation.</exception>
    /// <exception cref="KeyLoadException">The lane has insufficient retained-memory capacity.</exception>
    public McpMemoryLease Reserve(McpMemoryLane lane, long retainedBytes, CancellationToken cancellationToken = default)
    {
        var index = LaneIndex(lane);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedBytes);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapacity(index, retainedBytes);
            var lease = new McpMemoryLease(this, lane, retainedBytes);
            retained[index] += retainedBytes;
            return lease;
        }
    }

    /// <summary>Serializes growth with all reservations and releases against the same owning lock.</summary>
    /// <param name="lease">The reservation created by this budget.</param>
    /// <param name="totalBytes">The new nonnegative total retained-byte count.</param>
    /// <param name="cancellationToken">Cancels growth before accounting changes.</param>
    internal void Grow(McpMemoryLease lease, long totalBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(lease.Disposed, lease);
            if (totalBytes < lease.RetainedBytes)
            {
                throw new InvalidOperationException(ShrinkDetail);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var index = LaneIndex(lease.Lane);
            var additionalBytes = totalBytes - lease.RetainedBytes;
            EnsureCapacity(index, additionalBytes);
            retained[index] += additionalBytes;
            lease.RetainedBytes = totalBytes;
        }
    }

    /// <summary>Releases a reservation's current byte count once while serializing against concurrent growth.</summary>
    /// <param name="lease">The reservation created by this budget.</param>
    internal void Release(McpMemoryLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            if (lease.Disposed)
            {
                return;
            }

            var index = LaneIndex(lease.Lane);
            retained[index] -= lease.RetainedBytes;
            lease.Disposed = true;
        }
    }

    private void EnsureCapacity(int index, long additionalBytes)
    {
        if (additionalBytes > limits[index] - retained[index])
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CapacityDetail);
        }
    }

    private static int LaneIndex(McpMemoryLane lane)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)lane, (uint)McpMemoryLane.Ingress, nameof(lane));
        return (int)lane;
    }
}
