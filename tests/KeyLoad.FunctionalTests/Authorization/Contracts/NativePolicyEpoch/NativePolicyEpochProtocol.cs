namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochProtocol
{
    internal const string PartitionKey = "native-policy-partition";
    internal const string Root = "root";
    internal const string RootSecret = "native-policy-root.credential-secret-123456";
    internal const string Reader = "native-policy-reader";
    internal const string Collection = "native-policy-documents";
    internal const string Document = "original";
    internal const string Path = "/secret";
    internal const string Grant = "cache.policy.raw";
    internal const string UseGrant = "cache.policy.use";
    internal const string Classification = "private";
    internal const string Original = """{"public":"visible","secret":"native-policy-secret"}""";
    internal const string Updated = """{"public":"changed","secret":"native-policy-secret"}""";
    internal const string Hidden = """{"public":"visible"}""";
    internal const string UpdatedHidden = """{"public":"changed"}""";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string UnknownOwner = "A prior execution store owner has not released its original resources.";
    internal const long FirstRevision = 1;
    internal const long UpdatedRevision = 2;
    internal const int ColdCount = 2;
}
