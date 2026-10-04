namespace KeyLoad.Orleans;

/// <summary>Receives bounded private notifications from the real silo request path.</summary>
/// <remarks>The context is borrowed for the callback and must not be retained.</remarks>
internal interface IGrainRequestPhaseObserver
{
    ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken);

    void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context);
}
