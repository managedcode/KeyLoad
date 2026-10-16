using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed record SharedDomainRecoveryInput(ReplicatedOperation Original, ReplicatedOperation[] SeedOperations,
    OperationResult[] SeedResults, string[] SeedImage, long SeedPosition, StoreIdentity OriginalIdentityDigest, ReplicatedOperation[] ResourceCreations);
