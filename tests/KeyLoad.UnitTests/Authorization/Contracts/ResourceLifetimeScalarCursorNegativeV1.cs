using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

/// <summary>Unsupported historical input only; no current constructor or production fallback.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceLifetimeCursorNegativeProtocol.ScalarAlias)]
internal sealed record ResourceLifetimeScalarCursorNegativeV1(
    [property: Orleans.Id(QueryCursorContract.PurposeField)] string Purpose,
    [property: Orleans.Id(QueryCursorContract.IncarnationField)] Guid Incarnation,
    [property: Orleans.Id(QueryCursorContract.NodeIdField)] Guid NodeId,
    [property: Orleans.Id(QueryCursorContract.ReadGenerationField)] long ReadGeneration,
    [property: Orleans.Id(QueryCursorContract.PrincipalIdField)] string PrincipalId,
    [property: Orleans.Id(QueryCursorContract.PolicyEpochField)] long PolicyEpoch,
    [property: Orleans.Id(QueryCursorContract.SchemaVersionField)] long SchemaVersion,
    [property: Orleans.Id(QueryCursorContract.QueryHashField)] string QueryHash,
    [property: Orleans.Id(QueryCursorContract.CutPositionField)] long CutPosition,
    [property: Orleans.Id(QueryCursorContract.SourceEpochField)] long SourceEpoch,
    [property: Orleans.Id(QueryCursorContract.OffsetField)] int Offset,
    [property: Orleans.Id(QueryCursorContract.ExpiresAtField)] DateTimeOffset ExpiresAt);
