using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryInputReader
{
    internal static async Task<SharedDomainRecoveryInput> ReadAsync(string root, CancellationToken token)
        => new(await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, SharedDomainCrashProtocol.OperationFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation[]>(root, SharedDomainCrashProtocol.SeedOperationsFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<OperationResult[]>(root, SharedDomainCrashProtocol.SeedResultsFile, token),
            SharedDomainStoredImage.Format(await CommandIdempotencyCrashData.ReadEvidenceAsync<KeyValueRecord[]>(
                root, SharedDomainCrashProtocol.SeedImageFile, token)),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, SharedDomainCrashProtocol.SeedPositionFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<StoreIdentity>(root, SharedDomainCrashProtocol.IdentityDigestFile, token));
}
