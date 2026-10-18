namespace KeyLoad.RecoveryHost.Features.EventStreams;

internal static class SharedDomainCrashProtocol
{
    internal const string Mode = "shared-domain-producer-crash";
    internal const string OperationFile = "original-operation.bin";
    internal const string ResourceCreationsFile = "original-resource-creations.bin";
    internal const string SeedOperationsFile = "acknowledged-seed-operations.bin";
    internal const string SeedResultsFile = "acknowledged-seed-results.bin";
    internal const string SeedImageFile = "before-original-image.bin";
    internal const string SeedPositionFile = "before-original-position.bin";
    internal const string IdentityDigestFile = "before-original-identity-keydigest.bin";
    internal const string OriginalRootRequired = "The original shared-domain process root must be canonical, existing and nonempty.";
    internal const int FirstMutation = 0;
    internal const int MiddleMutation = 3;
    internal const int LaterMutation = 6;
}
