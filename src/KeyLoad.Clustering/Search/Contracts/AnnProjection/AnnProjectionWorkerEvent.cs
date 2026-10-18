namespace KeyLoad.Clustering;

[GenerateSerializer]
internal sealed record AnnProjectionWorkerEvent([property: Id(0)] ProjectionConsumerRef Consumer,
    [property: Id(1)] long Generation, [property: Id(2)] Guid NodeId);
