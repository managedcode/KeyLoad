using System.Text;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PreparationOwnershipOwner(string root, ZoneTreeStore store)
{
    internal static readonly byte[] SeedKey = Encoding.UTF8.GetBytes("execution-prepare/seed");
    internal static readonly byte[] ParentKey = Encoding.UTF8.GetBytes("execution-prepare/parent");
    internal static readonly byte[] ChildKey = Encoding.UTF8.GetBytes("execution-prepare/child");
    internal static readonly byte[] SeedValue = Encoding.UTF8.GetBytes("original");
    internal static readonly byte[] ParentValue = Encoding.UTF8.GetBytes("parent-stage");
    internal static readonly byte[] ChildValue = Encoding.UTF8.GetBytes("child-stage");
    internal ZoneTreeStore Store => store;
    internal byte[] Journal => File.ReadAllBytes(Path.Combine(root, ZoneTreePersistenceFormat.JournalFileName));

    internal static async Task RunAsync(Func<PreparationOwnershipOwner, Task> operation)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "keyload-execution-prepare-" + Guid.NewGuid().ToString("N"));
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            TestContext.Current!.Execution.CancellationToken.ThrowIfCancellationRequested();
            store = new(new(root), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            store.Commit((transaction, _) =>
            {
                transaction.Put(SeedKey, SeedValue);
                return true;
            });
            await operation(new(root, store));
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        if (failures.Count == 0 && Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
