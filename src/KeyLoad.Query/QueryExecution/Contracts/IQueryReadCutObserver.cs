namespace KeyLoad.Query;

/// <summary>Observes completed scalar query metadata synchronously after its original native read gate is released.</summary>
public interface IQueryReadCutObserver
{
    /// <summary>Receives immutable capture-time metadata without a storage view; publication is not a current-owner check.</summary>
    /// <param name="observation">Identity and authority scalars, never secrets or rows.</param>
    /// <param name="cancellationToken">The original read operation cancellation.</param>
    void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken);

    /// <summary>Observes a completed text-index wait after its original native read gate is released.</summary>
    /// <param name="observation">Capture-time owner, persisted authority and native applied horizon.</param>
    /// <param name="cancellationToken">The original wait cancellation.</param>
    void Observe(WaitForIndexReadCutObservation observation, CancellationToken cancellationToken)
    {
    }
}
