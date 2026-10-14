namespace KeyLoad.Clustering;

internal static class GrainRequestPhaseObservation
{
    internal static ValueTask ObserveAsync(IGrainRequestPhaseObserver? observer,
        DecodedGrainRequest request, GrainRequestPhase phase, IGrainContext? context,
        CancellationToken cancellationToken)
    {
        if (observer is null)
        {
            return ValueTask.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(context);
        return observer.ObserveAsync(GrainRequestProbeIdentity.From(request.Envelope), phase, context, cancellationToken);
    }

    internal static ValueTask ProducerDisposedAsync(IGrainRequestPhaseObserver? observer,
        GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext? context)
    {
        if (observer is null)
        {
            return ValueTask.CompletedTask;
        }
        ArgumentNullException.ThrowIfNull(context);
        if (observer is IGrainRequestProducerDisposedObserver asynchronous)
        {
            return asynchronous.ProducerDisposedAsync(identity, connectionId, context);
        }
        observer.ProducerDisposed(identity, context);
        return ValueTask.CompletedTask;
    }

    internal static void ProducerDisposed(IGrainRequestPhaseObserver? observer,
        GrainRequestProbeIdentity identity, IGrainContext? context)
    {
        if (observer is null)
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(context);
        observer.ProducerDisposed(identity, context);
    }
}
