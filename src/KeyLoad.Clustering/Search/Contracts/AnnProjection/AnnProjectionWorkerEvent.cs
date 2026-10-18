namespace KeyLoad.Clustering;

/// <summary>Carries disposable projection hints tied to the actual persisted physical node.</summary>
[GenerateSerializer]
public sealed record AnnProjectionWorkerEvent([property: Id(0)] ProjectionConsumerRef Consumer,
    [property: Id(1)] long Generation, [property: Id(2)] Guid NodeId);
