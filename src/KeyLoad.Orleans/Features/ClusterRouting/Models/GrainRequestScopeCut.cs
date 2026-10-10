namespace KeyLoad.Orleans;

internal readonly record struct GrainRequestScopeCut(Guid Incarnation,
    DateTimeOffset EvaluatedAt, TimeSpan MaximumFuture);
