namespace KeyLoad.Query.Features.ChangeFeeds;

internal static class LiveCursorContract
{
    internal const string Alias = "keyload.query.live-cursor.claims.v2";
    internal const string Purpose = "scalar-live-query-v2";
    internal const int CurrentVersion = 2;
    internal const int PurposeField = 0;
    internal const int QueryHashField = 1;
    internal const int ChangeCursorField = 2;
    internal const int LifetimeField = 3;
    internal const int VersionField = 4;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(LiveCursorContract.Alias)]
internal sealed record LiveCursorClaims(
    [property: global::Orleans.Id(LiveCursorContract.PurposeField)] string Purpose,
    [property: global::Orleans.Id(LiveCursorContract.QueryHashField)] string QueryHash,
    [property: global::Orleans.Id(LiveCursorContract.ChangeCursorField)] string ChangeCursor,
    [property: global::Orleans.Id(LiveCursorContract.LifetimeField)] ResourceLifetimeV1 Lifetime,
    [property: global::Orleans.Id(LiveCursorContract.VersionField)] int Version);
