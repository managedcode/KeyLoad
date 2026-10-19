namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CachePolicyEpochProtocol
{
    internal const string PartitionKey = "cache-policy-partition";
    internal const string Root = "root";
    internal const string RootSecret = "cache-policy-root.credential-secret-123456";
    internal const string Reader = "cache-policy-reader";
    internal const string Collection = "cache-policy-documents";
    internal const string Document = "original";
    internal const string Path = "/secret";
    internal const string Grant = "cache.policy.raw";
    internal const string UseGrant = "cache.policy.use";
    internal const string Classification = "private";
    internal const string Original = """{"public":"visible","secret":"cache-policy-secret"}""";
    internal const string Updated = """{"public":"changed","secret":"cache-policy-secret"}""";
    internal const string Hidden = """{"public":"visible"}""";
    internal const string UpdatedHidden = """{"public":"changed"}""";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string UnknownOwner = "A prior execution cache owner has not released its original resources.";
    internal const int Entries = 64;
    internal const int PoolEntries = 128;
    internal const int MaxValueBytes = 8192;
    internal const int MaxKeyBytes = 512;
    internal const long OwnerBytes = 262144;
    internal const long PoolBytes = 524288;
    internal const long FirstRevision = 1;
    internal const long UpdatedRevision = 2;
    internal const int ColdCount = 2;
}
