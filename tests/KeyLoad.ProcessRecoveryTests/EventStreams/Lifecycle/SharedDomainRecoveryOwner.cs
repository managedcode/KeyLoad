using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class SharedDomainRecoveryOwner
{
    private const string MissingResult = "The original shared-domain recovery owner returned no verified result.";

    internal static async Task<T> UseAsync<T>(string root, StoreIdentity expectedDigest, Func<DatabaseEngine, ZoneTreeStore, Task<T>> action)
        where T : class
    {
        SharedDomainRecoveryImage.RequireRoot(root);
        T? result = null;
        List<Exception> failures = [];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(root)
            {
                Incarnation = expectedDigest.Incarnation
            },
                RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SharedDomainRecoveryImage.IdentityAsync(store, expectedDigest);
                result = await action(SharedDomainRecoveryDatabase.Open(store), store);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResult);
    }
}
