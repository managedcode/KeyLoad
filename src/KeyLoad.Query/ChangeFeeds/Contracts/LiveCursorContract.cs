namespace KeyLoad.Query.Features.ChangeFeeds;

internal static class LiveCursorContract
{
    internal const string Alias = "keyload.query.live-cursor.claims";
    internal const string Purpose = "scalar-live-query";
    internal const int CurrentVersion = 2;
    internal const int PurposeField = 0;
    internal const int QueryHashField = 1;
    internal const int ChangeCursorField = 2;
    internal const int LifetimeField = 3;
    internal const int VersionField = 4;
}
