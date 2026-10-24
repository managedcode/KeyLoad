using KeyLoad.RecoveryTests.DocumentStorage;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.EventStreams;

internal static class SharedDomainRecoveryInputReader
{
    internal static async Task<SharedDomainRecoveryInput> ReadAsync(string root, CancellationToken token)
        => new(await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root, SharedDomainCrashProtocol.OperationFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>[]>(root, SharedDomainCrashProtocol.SeedOperationsFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<Result<object>[]>(root, SharedDomainCrashProtocol.SeedResultsFile, token),
            SharedDomainStoredImage.Format(await CommandIdempotencyCrashData.ReadEvidenceAsync<KeyValueRecord[]>(
                root, SharedDomainCrashProtocol.SeedImageFile, token)),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, SharedDomainCrashProtocol.SeedPositionFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<StoreIdentity>(root, SharedDomainCrashProtocol.IdentityDigestFile, token),
            await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>[]>(root, SharedDomainCrashProtocol.ResourceCreationsFile, token));
}
