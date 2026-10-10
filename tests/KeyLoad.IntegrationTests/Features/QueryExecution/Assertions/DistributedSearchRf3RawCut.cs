using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3RawCut
{
    private const string Database = "database";
    private const string Replica = "replica";
    private const string NodeLock = "node.owner.lock";
    private const string OwnerLock = "owner.lock";
    private const string Separator = ":";
    private const int HexExpansion = 2;
    private const int EmptyBytes = 0;
    private const string Overflow = "The complete distributed RF3 canonical image exceeded its original bound.";

    internal static async Task<string[][]> StopAndReadAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, NodeLock));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, Database, OwnerLock));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, Replica, OwnerLock));
        }
        var cuts = new List<string[]>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { token.ThrowIfCancellationRequested(); cuts.Add(Read(Path.Combine(root, node, Database))); }
        return [.. cuts];
    }

    private static string[] Read(string path)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        string[]? result = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(path), IntegrationExecutionOptions.StorageExecution(),
                IntegrationExecutionOptions.PointCacheExecution());
            var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
            result = store.Read(view =>
            {
                var rows = view.Scan([], limits.MaxScanRecords);
                if (rows.HasMore)
                { throw new InvalidOperationException(Overflow); }
                var bytes = EmptyBytes;
                foreach (var row in rows.Records)
                {
                    bytes = checked(bytes + checked((row.Key.Length + row.Value.Length) * HexExpansion) + Separator.Length);
                    if (bytes > limits.MaxBatchBytes)
                    { throw new InvalidOperationException(Overflow); }
                }
                return rows.Records.Select(row => Convert.ToHexString(row.Key.Span) + Separator
                    + Convert.ToHexString(row.Value.Span)).Prepend(store.Position.ToString(CultureInfo.InvariantCulture)).ToArray();
            });
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(Overflow);
    }
}
