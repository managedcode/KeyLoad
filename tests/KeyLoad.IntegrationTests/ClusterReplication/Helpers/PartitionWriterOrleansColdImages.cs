using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Captures complete actual six-owner native images only after the original processes joined.</summary>
internal static class PartitionWriterOrleansColdImages
{
    private const string NodeOwnerLock = "node.owner.lock";
    private const string RowSeparator = ":";

    internal static PartitionWriterOrleansImage[] Capture(TwoRf3MembershipWave wave)
        => TwoRf3MembershipProtocol.Nodes.Select(node => Capture(wave, node)).ToArray();

    private static PartitionWriterOrleansImage Capture(TwoRf3MembershipWave wave, string node)
    {
        var root = Path.Combine(wave.OwnedDataRoot, node);
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, NodeOwnerLock));
        var failures = new List<Exception>();
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        PartitionWriterOrleansImage? image = null;
        ServerFailureObserver.Observe(() =>
        {
            canonical = PartitionWriterOrleansStores.Open(root, PartitionWriterOrleansStores.CanonicalDirectory);
            replica = PartitionWriterOrleansStores.Open(root, PartitionWriterOrleansStores.ReplicaDirectory);
            var canonicalRows = Rows(canonical);
            var replicaRows = Rows(replica);
            image = new(node, canonicalRows, canonical.Position, NativeSerialization.Serialize(canonical.Identity),
                replicaRows, replica.Position, NativeSerialization.Serialize(replica.Identity));
        }, failures);
        if (replica is { } replicaOwner)
        {
            ServerFailureObserver.Observe(replicaOwner.Dispose, failures);
        }
        if (canonical is { } canonicalOwner)
        {
            ServerFailureObserver.Observe(canonicalOwner.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return image ?? throw new InvalidOperationException(PartitionWriterOrleansProtocol.IncompleteImage);
    }

    private static string[] Rows(ZoneTreeStore store)
        => store.Read(view =>
        {
            var scan = view.Scan([], IntegrationExecutionOptions.DatabaseLimits().Value.MaxScanRecords);
            if (scan.HasMore)
            {
                throw new InvalidOperationException(PartitionWriterOrleansProtocol.IncompleteImage);
            }
            return scan.Records.Select(row => Convert.ToHexString(row.Key.Span) + RowSeparator
                + Convert.ToHexString(row.Value.Span)).ToArray();
        });
}
