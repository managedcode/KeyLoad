using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NodeEpochCoordinatorAssertions
{
    private const string LaterTenant = "node-epoch-tenant";
    private const string LaterDatabase = "node-epoch-database";
    private const string LaterDomain = "node-epoch-domain";
    private const string LaterResource = "post-publication";
    private const string LaterPartition = "partition";
    private const string RootPrincipal = "root";
    private const long NodeSnapshotRecordCount = 3;

    internal static async Task AssertHistoricalImagesAsync(string source, string destination,
        EpochPriorProbeReceipt sourceReceipt, CancellationToken cancellationToken)
    {
        var sourceImages = Directory.GetFiles(Path.Combine(source, "snapshots"), "*.snapshot")
            .Order(StringComparer.Ordinal).ToArray();
        var targetImages = Directory.GetFiles(Path.Combine(destination, "snapshots"), "*.snapshot")
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(targetImages.Select(Path.GetFileName)).IsEquivalentTo(sourceImages.Select(Path.GetFileName));
        await Assert.That(sourceImages.Length).IsEqualTo(3);
        foreach (var sourceImage in sourceImages)
        {
            var targetImage = Path.Combine(destination, "snapshots", Path.GetFileName(sourceImage));
            var imageReceipt = await EpochPriorExecutableFixture.VerifySnapshotAsync(
                Path.Combine(source, ServerNodeUpgradeProtocol.Canonical), sourceImage, cancellationToken,
                sourceReceipt.DataEpoch);
            var verifierRoot = Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("The target node has no trial root.");
            await AssertCurrentImageAsync(verifierRoot, targetImage, imageReceipt);
        }
    }

    internal static async Task ApplyLaterOperationAsync(EpochPriorNodeProfile profile, string destination,
        CancellationToken cancellationToken)
    {
        var canonicalPath = Path.Combine(destination, ServerNodeUpgradeProtocol.Canonical);
        var replicaPath = Path.Combine(destination, ServerNodeUpgradeProtocol.Replica);
        using var canonical = new ZoneTreeStore(NodeEpochComponentProfile.CanonicalStoreOptions(profile, canonicalPath), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(NodeEpochComponentProfile.CanonicalStoreOptions(profile, replicaPath), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        var configuration = new ReplicaConfiguration(profile.LocalId, [.. profile.Voters], destination,
            profile.Incarnation);
        using var log = new DurableReplicaLog(replica, RecoveryExecutionOptions.Configuration(configuration), canonicalDatabase: database);
        var snapshots = new ReplicaSnapshotStore(canonical, log, RecoveryExecutionOptions.Configuration(configuration), RecoveryExecutionOptions.Replica());
        await using var materializer = new ReplicaMaterializer(database, log, snapshots, RecoveryExecutionOptions.Replica());
        var resource = new ResourceDefinition(LaterResource, ResourceKind.Collection, LaterDomain);
        var request = new ConfigureResourceRequest(LaterTenant, LaterDatabase, resource);
        var operation = database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), RootPrincipal,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var index = log.State.LastIndex + 1;
        log.Append([new(index, log.State.Term, operation)]);
        materializer.Commit(index);
        await materializer.WaitForApplyAsync(index, cancellationToken);
        _ = snapshots.Create(index, log.State.Term);
    }

    internal static async Task AssertLaterResourceAsync(EpochPriorNodeProfile profile, string destination)
    {
        using var canonical = new ZoneTreeStore(NodeEpochComponentProfile.CanonicalStoreOptions(profile,
            Path.Combine(destination, ServerNodeUpgradeProtocol.Canonical)), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(NodeEpochComponentProfile.CanonicalStoreOptions(profile,
            Path.Combine(destination, ServerNodeUpgradeProtocol.Replica)), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        var configuration = new ReplicaConfiguration(profile.LocalId, [.. profile.Voters], destination,
            profile.Incarnation);
        using var log = new DurableReplicaLog(replica, RecoveryExecutionOptions.Configuration(configuration), canonicalDatabase: database);
        var partition = new PartitionRef(LaterTenant, LaterDatabase, LaterDomain, LaterPartition);
        var actual = canonical.Read(view => database.Resource(view, partition, LaterResource, ResourceKind.Collection));
        await Assert.That(actual.Name).IsEqualTo(LaterResource);
        await Assert.That(database.LastApplied).IsEqualTo(4L);
        await Assert.That(log.State.CommittedIndex).IsEqualTo(4L);
        await Assert.That(log.State.Snapshot?.Index).IsEqualTo(4L);
    }

    private static async Task AssertCurrentImageAsync(string verifierRoot, string path, EpochPriorProbeReceipt prior)
    {
        await Assert.That(prior.ErrorCode).IsNull();
        await Assert.That(prior.AppliedPosition).IsGreaterThan(0);
        var directory = Path.Combine(verifierRoot, "image-verifier-" + Guid.NewGuid().ToString("N"));
        using var verifier = new ZoneTreeStore(new(directory) { Incarnation = prior.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var current = verifier.VerifySnapshot(path);
        await Assert.That(current.Incarnation).IsEqualTo(prior.Incarnation);
        await Assert.That(current.Position).IsEqualTo(prior.Position);
        await Assert.That(current.AppliedPosition).IsEqualTo(prior.AppliedPosition);
        await Assert.That(current.RecordCount).IsEqualTo(NodeSnapshotRecordCount);
    }
}
