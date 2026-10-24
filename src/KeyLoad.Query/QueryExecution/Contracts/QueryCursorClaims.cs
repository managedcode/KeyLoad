namespace KeyLoad.Query.QueryExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueryCursorContract.Alias)]
internal sealed record QueryCursorClaims(
    [property: global::Orleans.Id(QueryCursorContract.PurposeField)] string Purpose,
    [property: global::Orleans.Id(QueryCursorContract.IncarnationField)] Guid Incarnation,
    [property: global::Orleans.Id(QueryCursorContract.NodeIdField)] Guid NodeId,
    [property: global::Orleans.Id(QueryCursorContract.ReadGenerationField)] long ReadGeneration,
    [property: global::Orleans.Id(QueryCursorContract.PrincipalIdField)] string PrincipalId,
    [property: global::Orleans.Id(QueryCursorContract.PolicyEpochField)] long PolicyEpoch,
    [property: global::Orleans.Id(QueryCursorContract.SchemaVersionField)] long SchemaVersion,
    [property: global::Orleans.Id(QueryCursorContract.QueryHashField)] string QueryHash,
    [property: global::Orleans.Id(QueryCursorContract.CutPositionField)] long CutPosition,
    [property: global::Orleans.Id(QueryCursorContract.SourceEpochField)] long SourceEpoch,
    [property: global::Orleans.Id(QueryCursorContract.OffsetField)] int Offset,
    [property: global::Orleans.Id(QueryCursorContract.ExpiresAtField)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(QueryCursorContract.LifetimeField)] ResourceLifetime Lifetime,
    [property: global::Orleans.Id(QueryCursorContract.VersionField)] int Version);
