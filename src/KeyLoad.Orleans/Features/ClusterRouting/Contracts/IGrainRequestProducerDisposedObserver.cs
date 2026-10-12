namespace KeyLoad.Orleans;

/// <summary>Joins original producer disposal notification after actual enumerator disposal.</summary>
internal interface IGrainRequestProducerDisposedObserver
{
    ValueTask ProducerDisposedAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext context);
}
