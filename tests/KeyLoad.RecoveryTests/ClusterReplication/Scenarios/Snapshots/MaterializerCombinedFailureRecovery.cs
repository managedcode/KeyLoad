using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.RecoveryTests.MaterializerCombinedFailureProtocol;

namespace KeyLoad.RecoveryTests;

internal static class MaterializerCombinedFailureRecovery
{
    internal static async Task RunAsync(MaterializerCombinedFailureFixture fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var identity = NativeSerialization.Serialize(fixture.Database.Store.Identity);
        fixture.CloseStores();
        using var canonical = new ZoneTreeStore(new(Path.Combine(fixture.Directory, FaultCanonicalDirectory))
        {
            Incarnation = fixture.Configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var replica = new ZoneTreeStore(new(fixture.Configuration.Directory)
        {
            Incarnation = fixture.Configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var db = MaterializerCombinedFailureFixture.OpenDatabase(canonical);
        using var log = new DurableReplicaLog(replica, RecoveryExecutionOptions.Configuration(fixture.Configuration), canonicalDatabase: db);
        await using var materializer = new ReplicaMaterializer(db, log, new ReplicaSnapshotStore(canonical, log,
            RecoveryExecutionOptions.Configuration(fixture.Configuration), RecoveryExecutionOptions.Replica(), replacementToken => db.RequireNoRetirementForSnapshotReplacement(
                    new(db.OperationLimitsOptions, db.EvaluationClock, replacementToken))), RecoveryExecutionOptions.Replica());
        await materializer.WaitForApplyAsync(FaultIndex, token);
        await RecordedQueueFollowerAssertions.EqualAsync(db.LastApplied, FaultIndex);
        await RecordedQueueFollowerAssertions.EqualAsync(canonical.Position, fixture.ExpectedRecoveredPosition);
        await RecordedQueueFollowerAssertions.EqualAsync(NativeSerialization.Serialize(canonical.Identity), identity);
        await RecordedQueueFollowerAssertions.EqualAsync(log.ReadEntry(FaultIndex), new ReplicaEntry(FaultIndex, Term, null));
        await RecordedQueueFollowerAssertions.EqualAsync(log.State.CommittedIndex, FaultIndex);
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId);
        var operation = db.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), Principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)));
        var configured = new ReplicaEntry(HealthyConfigurationIndex, Term, operation);
        log.Append([configured]);
        materializer.Commit(HealthyConfigurationIndex);
        await materializer.WaitForApplyAsync(HealthyConfigurationIndex, token);
        await RecordedQueueFollowerAssertions.EqualAsync(OutcomeStoreOracle.Read(canonical, operation)!.Get<ResourceDefinition>(), resource);
        var healthy = await EnqueueAsync(db, log, materializer, token);
        await materializer.DisposeAsync();
        var image = RecordedQueueFollowerNative.Image(canonical);
        var replicaImage = RecordedQueueFollowerNative.Image(replica);
        var position = canonical.Position;
        var replicaPosition = replica.Position;
        log.Dispose();
        canonical.Dispose();
        replica.Dispose();
        await MaterializerCombinedFailureSecondCold.RunAsync(fixture, identity, image, replicaImage,
            position, replicaPosition, configured, healthy, token);
    }

    private static async Task<(ReplicaEntry Entry, CommitReceipt Receipt)> EnqueueAsync(DatabaseEngine db,
        DurableReplicaLog log, ReplicaMaterializer materializer, CancellationToken token)
    {
        var message = new EnqueueMessage(Queue, Message, Payload, Headers);
        var request = new CommandRequest(Guid.NewGuid(), Partition, [message]);
        var placement = db.ReadAtomicPartitionPlacement(Principal, new(PlacementVersion, Partition));
        var expected = new CommitReceipt(request.CommandId, new CommitToken(placement.Incarnation,
            Partition.AtomicPartitionId, HealthyEnqueueIndex, placement.PlacementEpoch),
            [new(EnqueueReceipt, Queue, Message, InitialStateVersion)], DurabilityProfile.ProcessDurable);
        var operation = db.CreateNativeOperation(OperationKind.Batch, request.CommandId, Principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        var entry = new ReplicaEntry(HealthyEnqueueIndex, Term, operation);
        log.Append([entry]);
        materializer.Commit(HealthyEnqueueIndex);
        await materializer.WaitForApplyAsync(HealthyEnqueueIndex, token);
        var actual = OutcomeStoreOracle.Read(db.Store, operation)!.Get<CommitReceipt>();
        await RecordedQueueFollowerAssertions.EqualAsync(actual, expected);
        await MaterializerCombinedFailureSecondCold.HealthyImageAsync(db, message);
        return (entry, actual);
    }
}
