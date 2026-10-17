namespace KeyLoad.Clustering;

/// <summary>Observes only admitted current native metadata within the original joined owner.</summary>
public interface IReplicaNativeDiscoveryObserver
{
    /// <summary>Returns bounded metadata after an optional explicitly armed test observation.</summary>
    /// <param name="request">The authenticated immutable native request.</param>
    /// <param name="state">The actual current local metadata.</param>
    /// <param name="originalPayload">The generated actual metadata bytes.</param>
    /// <param name="cancellationToken">The same admitted native operation cancellation.</param>
    /// <returns>Original bytes or the scoped bounded test observation result.</returns>
    ReadOnlyMemory<byte> TransformNativeDiscovery(ReplicaNativeDiscoveryRequest request, ReplicaSiloDiscovery state,
        ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken);
    /// <summary>Awaits a protected fixture callback under the same admitted native lifetime.</summary>
    /// <param name="request">The original authenticated request.</param>
    /// <param name="state">The actual current local metadata.</param>
    /// <param name="originalPayload">The original generated metadata bytes.</param>
    /// <param name="cancellationToken">The original bounded admitted cancellation.</param>
    /// <returns>The original synchronous observation unless explicitly overridden.</returns>
    ValueTask<ReadOnlyMemory<byte>> TransformNativeDiscoveryAsync(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery state, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
        => new(TransformNativeDiscovery(request, state, originalPayload, cancellationToken));
}
