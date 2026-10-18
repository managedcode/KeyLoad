using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Intercepts only an authenticated Batch that dispatches a remote queue transfer.</summary>
internal interface IControlledDocumentCommandRouter
{
    /// <summary>Returns the original transfer outcome, or null when the Batch takes the ordinary route.</summary>
    /// <param name="envelope">Original independently verified request envelope.</param>
    /// <param name="payload">Original native Batch bytes held stable by the request owner.</param>
    /// <param name="binding">The required original native operation binding and shared result-retention ledger.</param>
    /// <param name="cancellationToken">Original request cancellation and absolute deadline.</param>
    /// <returns>The genuine original outcome or the unchanged ordinary-route decision.</returns>
    Task<OperationResult?> TryExecuteAsync(GrainRequestEnvelope envelope, ReadOnlyMemory<byte> payload,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);
}
