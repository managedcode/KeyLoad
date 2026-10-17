using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Opens only the actual stopped Orleans owner's two native stores, never a substitute topology.</summary>
internal static class PartitionWriterOrleansStores
{
    internal const string CanonicalDirectory = "database";
    internal const string ReplicaDirectory = "replica";

    internal static async Task UseAsync(TwoRf3MembershipWave wave, string node,
        PartitionMovementPublicParentRf3Seed seed,
        Func<ZoneTreeStore, ZoneTreeStore, DurableReplicaLog, DatabaseEngine, Task> operation)
    {
        var root = Path.Combine(wave.OwnedDataRoot, node);
        NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
        var failures = new List<Exception>();
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        DurableReplicaLog? log = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            canonical = Open(root, CanonicalDirectory);
            replica = Open(root, ReplicaDirectory);
            var options = NodeEpochRf3OfflineOptions.Create(root, wave.Profile, node);
            var configured = options.CreateReplicaConfiguration(root);
            var owner = seed.Directory.ControlOwner;
            if (configured.Incarnation != owner.Incarnation
                || !configured.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(PartitionWriterOrleansProtocol.OwnerMismatch);
            }
            var database = Database(canonical, owner);
            log = new(replica, Options.Create(configured), canonicalDatabase: database);
            await operation(canonical, replica, log, database).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (log is { } journal)
        {
            ServerFailureObserver.Observe(journal.Dispose, failures);
        }
        if (replica is { } replicaOwner)
        {
            ServerFailureObserver.Observe(replicaOwner.Dispose, failures);
        }
        if (canonical is { } canonicalOwner)
        {
            ServerFailureObserver.Observe(canonicalOwner.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static ZoneTreeStore Open(string root, string directory)
        => new(new(Path.Combine(root, directory)), IntegrationExecutionOptions.StorageExecution(),
            IntegrationExecutionOptions.PointCacheExecution());

    private static DatabaseEngine Database(ZoneTreeStore canonical, PhysicalShardRecord owner)
        => new(canonical, new AuthorizationPolicy(), IntegrationExecutionOptions.DatabaseLimits(),
            IntegrationExecutionOptions.DueWork(), IntegrationExecutionOptions.EventSource(),
            IntegrationExecutionOptions.Messaging(), IntegrationExecutionOptions.GraphExecution(),
            IntegrationExecutionOptions.ChangeFeedExecution(), IntegrationExecutionOptions.BlobExecution(),
            IntegrationExecutionOptions.NativeClaimsExecution(), IntegrationExecutionOptions.TimeSeriesExecution(),
            IntegrationExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance,
            physicalOwner: owner);
}
