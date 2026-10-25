using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeCursorDrift
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, QueryEngine engine, string sql,
        AstQueryRequest liveQuery, string scalar, string live, ResourceLifetime otherActualCreation)
    {
        var partition = owner.Source.Partition;
        var key = ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId,
            SafeProjectorOperationProtocol.Collection);
        var original = owner.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException();
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                // Negative current-pair drift only. The replacement came from another genuine execution creation.
                // No business-read success or retirement acknowledgement is allowed under this injected pair.
                Set(owner, key, OrleansSerialization.Serialize(otherActualCreation));
                await ResourceLifetimeCursorRefusals.CurrentAsync(owner, engine, sql, liveQuery, scalar, live);
            }, failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => Set(owner, key, original), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Set(SafeProjectorOperationDatabase owner, byte[] key, byte[] value)
        => owner.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });
}
