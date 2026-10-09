namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveRf3Protocol
{
    internal const string TenantPrefix = "feed-tenant-";
    internal const string PrincipalPrefix = "feed-reader-";
    internal const string SecretGrant = "feed.secret.read";
    internal const string SecretTag = "private";
    internal const string MatchingStatus = "open";
    internal const string Collection = "feed-orders";
    internal const string Database = "feed-database";
    internal const string Domain = "feed-domain";
    internal const string Feed = "keyload_changes_read";
    internal const string LiveStart = "keyload_query_live_start";
    internal const string LiveRead = "keyload_query_live_read";
    internal const string Secret = "feed-private-canary";
    internal const string SecretField = "/secret";
    internal const string First = "a";
    internal const string Hidden = "b";
    internal const string Owner = "feed-owner";
    internal const string OtherOwner = "other-owner";
    internal const string FirstJson = "{\"number\":1,\"status\":\"open\",\"secret\":\"feed-private-canary\"}";
    internal const string UpdatedJson = "{\"number\":2,\"status\":\"open\",\"secret\":\"feed-private-canary\"}";
    internal const string FirstProjected = "{\"number\":1,\"status\":\"open\"}";
    internal const string UpdatedProjected = "{\"number\":2,\"status\":\"open\"}";
    internal const long FirstRevision = 1;
    internal const long UpdatedRevision = 2;
    internal const long DeletedRevision = 3;
    internal const decimal UpdatedNumber = 2m;
    internal const long FirstSequence = 1;
    internal const long UpdatedSequence = 3;
    internal const long DeletedSequence = 4;
    internal const int PageLimit = 1;
    internal const long EpochStep = 1;
    internal const string ColdScenario = "feed-live-cold-owner";
}
