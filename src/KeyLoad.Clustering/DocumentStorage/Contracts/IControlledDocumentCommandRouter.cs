using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Intercepts only an authenticated Batch with durable protected publication.</summary>
internal interface IControlledDocumentCommandRouter
{
    /// <summary>Returns the original A outcome, or null only when no protected publication exists.</summary>
    /// <param name="envelope">Original independently verified request envelope.</param>
    /// <param name="payload">Original native Batch bytes held stable by the request owner.</param>
    /// <param name="binding">The required original native operation binding and shared result-retention ledger.</param>
    /// <param name="cancellationToken">Original request cancellation and absolute deadline.</param>
    /// <param name="grantSettled">Optional bounded post-settlement observer borrowed from the actual original request grain.</param>
    /// <param name="outcomeObserved">Optional bounded observer borrowed from the actual original request grain.</param>
    /// <returns>The genuine original outcome or the unchanged ordinary-route decision.</returns>
    Task<OperationResult?> TryExecuteAsync(GrainRequestEnvelope envelope, ReadOnlyMemory<byte> payload,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken, Func<CancellationToken, ValueTask>? grantSettled = null,
        Func<CancellationToken, ValueTask>? outcomeObserved = null);
}
