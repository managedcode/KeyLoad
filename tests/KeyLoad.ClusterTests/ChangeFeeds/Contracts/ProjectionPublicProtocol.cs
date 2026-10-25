namespace KeyLoad.ClusterTests.ChangeFeeds;

internal static class ProjectionPublicProtocol
{
    internal const string TenantPrefix = "projection-public-";
    internal const string Database = "projection-public-db";
    internal const string Domain = "projection-public-domain";
    internal const string Source = "feed-source";
    internal const string Target = "feed-derived";
    internal const string Consumer = "projection";
    internal const string NextConsumer = "next-projection";
    internal const string IdentityPrefix = "projection-admin-";
    internal const string KeyPrefix = "projection-key-";
    internal const string Separator = ".";
    internal const string GuidFormat = "N";
    internal const string InputJson = "{\"n\":1}";
    internal const string EffectJson = "{\"sourceCount\":1}";
    internal const string SafeFailure = "The database operation could not be completed.";
    internal const string PutKind = "putDocument";
    internal const int SecretBytes = 32;
    internal const int ColdCuts = 2;
}
