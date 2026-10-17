namespace KeyLoad.CrashHost.Features.EventStreams;

internal static class SharedDomainCrashProtocol
{
    internal const string Mode = "kl081-shared-domain-producer-crash";
    internal const string OperationFile = "kl081-original-operation.bin";
    internal const string ResourceCreationsFile = "kl081-original-resource-creations.bin";
    internal const string SeedOperationsFile = "kl081-acknowledged-seed-operations.bin";
    internal const string SeedResultsFile = "kl081-acknowledged-seed-results.bin";
    internal const string SeedImageFile = "kl081-before-original-image.bin";
    internal const string SeedPositionFile = "kl081-before-original-position.bin";
    internal const string IdentityDigestFile = "kl081-before-original-identity-keydigest.bin";
    internal const string OriginalRootRequired = "The original shared-domain process root must be canonical, existing and nonempty.";
    internal const int FirstMutation = 0;
    internal const int MiddleMutation = 3;
    internal const int LaterMutation = 6;
}
