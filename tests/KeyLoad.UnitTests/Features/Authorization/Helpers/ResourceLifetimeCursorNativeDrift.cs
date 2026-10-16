using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeCursorNativeDrift
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, QueryEngine engine, string sql,
        AstQueryRequest liveQuery, string scalar, string live, ResourceLifetimeV1 otherActualCreation)
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
                // Negative current-pair drift only. The replacement came from another genuine native creation.
                // No business-read success or retirement acknowledgement is allowed under this injected pair.
                Set(owner, key, NativeSerialization.Serialize(otherActualCreation));
                await ResourceLifetimeCursorNativeRefusals.CurrentAsync(owner, engine, sql, liveQuery, scalar, live);
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
