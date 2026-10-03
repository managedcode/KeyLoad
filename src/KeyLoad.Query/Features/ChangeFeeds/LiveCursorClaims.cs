namespace KeyLoad.Query.Features.ChangeFeeds;

internal static class LiveCursorContract
{
    internal const string Alias = "keyload.query.live-cursor.claims.v1";
    internal const string Purpose = "scalar-live-query";
    internal const int PurposeField = 0;
    internal const int QueryHashField = 1;
    internal const int ChangeCursorField = 2;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(LiveCursorContract.Alias)]
internal sealed record LiveCursorClaims(
    [property: global::Orleans.Id(LiveCursorContract.PurposeField)] string Purpose,
    [property: global::Orleans.Id(LiveCursorContract.QueryHashField)] string QueryHash,
    [property: global::Orleans.Id(LiveCursorContract.ChangeCursorField)] string ChangeCursor);
