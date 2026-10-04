namespace KeyLoad.Query.Features.QueryExecution;

internal static class QueryCursorContract
{
    internal const string Alias = "keyload.query.cursor.claims.v1";
    internal const int PurposeField = 0;
    internal const int IncarnationField = 1;
    internal const int NodeIdField = 2;
    internal const int ReadGenerationField = 3;
    internal const int PrincipalIdField = 4;
    internal const int PolicyEpochField = 5;
    internal const int SchemaVersionField = 6;
    internal const int QueryHashField = 7;
    internal const int CutPositionField = 8;
    internal const int SourceEpochField = 9;
    internal const int OffsetField = 10;
    internal const int ExpiresAtField = 11;
}

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
    [property: global::Orleans.Id(QueryCursorContract.ExpiresAtField)] DateTimeOffset ExpiresAt);
