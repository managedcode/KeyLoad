using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityIdentity
{
    private const string OriginalOwnersRequired = "All original AppHost storage owners must join before native identity reads.";
    private const string MissingIdentity = "The original native storage identity is unavailable.";
    private const string DatabaseDirectory = "database";

    internal static byte[][] Capture(TwoRf3MembershipWave wave)
    {
        if (!wave.applicationDisposed || !wave.nodeLocksReleased || wave.application is not null)
        {
            throw new InvalidOperationException(OriginalOwnersRequired);
        }
        var failures = new List<Exception>();
        var identities = new List<byte[]>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            ServerFailureObserver.Observe(() => identities.Add(One(Path.Combine(wave.OwnedDataRoot, node, DatabaseDirectory))), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return [.. identities];
    }
    private static byte[] One(string path)
    {
        ZoneTreeStore? store = null;
        byte[]? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(path), IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            result = NativeSerialization.Serialize(store.Identity);
        }, failures);
        if (store is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingIdentity);
    }
}
