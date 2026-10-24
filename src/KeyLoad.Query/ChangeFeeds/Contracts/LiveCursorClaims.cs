namespace KeyLoad.Query.ChangeFeeds;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(LiveCursorContract.Alias)]
internal sealed record LiveCursorClaims(
    [property: global::Orleans.Id(LiveCursorContract.PurposeField)] string Purpose,
    [property: global::Orleans.Id(LiveCursorContract.QueryHashField)] string QueryHash,
    [property: global::Orleans.Id(LiveCursorContract.ChangeCursorField)] string ChangeCursor,
    [property: global::Orleans.Id(LiveCursorContract.LifetimeField)] ResourceLifetime Lifetime,
    [property: global::Orleans.Id(LiveCursorContract.VersionField)] int Version);
