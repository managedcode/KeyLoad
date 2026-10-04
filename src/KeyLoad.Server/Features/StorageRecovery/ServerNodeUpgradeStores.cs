using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

internal sealed class ServerNodeUpgradeStores : IDisposable
{
    private Exception? primaryFailure;

    private ServerNodeUpgradeStores(string directory, NodeOptions options)
    {
        Canonical = new(ServerNodeUpgradeAuthority.StoreOptions(Path.Combine(directory, ServerNodeUpgradeProtocol.Canonical), options));
        try
        { Replica = new(ServerNodeUpgradeAuthority.StoreOptions(Path.Combine(directory, ServerNodeUpgradeProtocol.Replica), options)); }
        catch (Exception primary)
        {
            try
            { Canonical.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    internal ZoneTreeStore Canonical { get; }
    internal ZoneTreeStore Replica { get; }

    internal static T Run<T>(string directory, NodeOptions options, Func<ServerNodeUpgradeStores, T> action)
    {
        using var stores = new ServerNodeUpgradeStores(directory, options);
        try
        { return action(stores); }
        catch (Exception failure) { stores.primaryFailure = failure; throw; }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Replica.Dispose, failures);
        ServerFailureObserver.Observe(Canonical.Dispose, failures);
        if (failures.Count > 0 && primaryFailure is not null)
        { failures.Insert(0, primaryFailure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
