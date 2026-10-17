using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeCursorNegativeProtocol
{
    internal const string ScalarAlias = "keyload.query.cursor.claims.v1";
    internal const string LiveAlias = "keyload.query.live-cursor.claims.v1";
    internal const string ScalarPurpose = "query-page";
    internal const string LivePurpose = "scalar-live-query";
    internal const int PurposeField = 0;
    internal const int QueryHashField = 1;
    internal const int ChangeCursorField = 2;
}

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

/// <summary>Unsupported historical live input; signed with the existing actual native database signer.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ResourceLifetimeCursorNegativeProtocol.LiveAlias)]
internal sealed record ResourceLifetimeLiveCursorNegativeV1(
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.PurposeField)] string Purpose,
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.QueryHashField)] string QueryHash,
    [property: Orleans.Id(ResourceLifetimeCursorNegativeProtocol.ChangeCursorField)] string ChangeCursor);
