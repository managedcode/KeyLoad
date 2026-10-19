namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeCursorNegativeProtocol
{
    internal const string ScalarAlias = "keyload.query.cursor.claims";
    internal const string LiveAlias = "keyload.query.live-cursor.claims";
    internal const string ScalarPurpose = "query-page";
    internal const string LivePurpose = "scalar-live-query";
    internal const int PurposeField = 0;
    internal const int QueryHashField = 1;
    internal const int ChangeCursorField = 2;
}
