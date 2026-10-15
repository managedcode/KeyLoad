using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.ClusterReplication.CoordinatorShutdownProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class CoordinatorShutdownNativeAdmission
{
    internal static async Task RunAsync(TestDatabase db, CoordinatorShutdownStorage storage, CancellationToken token)
    {
        ZoneTreeStore? replica = null;
        DurableReplicaLog? log = null;
        ReplicaMaterializer? applying = null;
        ReplicaConsensus? consensus = null;
        ClusterCoordinator? coordinator = null;
        Task<OperationResult>? submitted = null;
        Exception? expected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var configuration = UnitExecutionOptions.ReplicaConfiguration(new(
                KeyLoad.UnitTests.Features.ClusterRouting.PhysicalShardCatalogVoterIds.First,
                KeyLoad.UnitTests.Features.ClusterRouting.PhysicalShardCatalogVoterIds.Standard, Path.Combine(db.Directory, P.ReplicaDirectory), db.Store.Identity.Incarnation));
            replica = new(new(configuration.Value.Directory)
            {
                Incarnation = configuration.Value.Incarnation
            }, db.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            log = new(replica, configuration, canonicalDatabase: db.Database);
            var execution = UnitExecutionOptions.ReplicaExecution();
            applying = new(db.Database, log, new ReplicaSnapshotStore(db.Store, log, configuration, execution), execution);
            consensus = new(applying, configuration, execution, TimeProvider.System);
            var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
            coordinator = new(consensus, db.Database, governor, TimeProvider.System, execution, UnitAdmissionOptions.Inbox());
            await coordinator.StartAsync(token);
            var image = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(db.Store);
            var position = db.Store.Position;
            var command = new CommandRequest(Guid.NewGuid(), db.Partition, [new PutDocument(P.Collection, P.Interrupted, P.Json)]);
            submitted = coordinator.SubmitAsync(OperationKind.Batch, command.CommandId, P.Root,
                System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command)), token);
            await Assert.That(governor.Snapshot().Commands).IsEqualTo(P.ActiveCommands);
            await coordinator.StopAsync(token);
            expected = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => submitted.WaitAsync(token));
            await Assert.That(((KeyLoadException)expected!).Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(governor.Snapshot()).IsEqualTo(P.EmptyAdmission);
            await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, position);
            var disposal = coordinator.DisposeAsync().AsTask();
            await disposal.WaitAsync(token);
            await Assert.That(coordinator.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
            await coordinator.StopAsync(token);
        }, failures);
        await CloseAsync(storage, coordinator, consensus, applying, log, replica, submitted, expected, failures);
    }

    private static async Task CloseAsync(CoordinatorShutdownStorage storage, ClusterCoordinator? coordinator,
        ReplicaConsensus? consensus, ReplicaMaterializer? applying, DurableReplicaLog? log, ZoneTreeStore? replica,
        Task<OperationResult>? submitted, Exception? expected, List<Exception> failures)
    {
        var closeFailures = new List<Exception>();
        if (coordinator is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => coordinator.DisposeAsync().AsTask(), closeFailures);
        }
        if (submitted is not null)
        {
            await ObserveSubmissionAsync(submitted, expected, failures);
        }
        if (consensus is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => consensus.DisposeAsync().AsTask(), closeFailures);
        }
        if (applying is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => applying.DisposeAsync().AsTask(), closeFailures);
        }
        if (log is not null)
        {
            ServerFailureObserver.Observe(log.Dispose, closeFailures);
        }
        if (replica is not null)
        {
            ServerFailureObserver.Observe(replica.Dispose, closeFailures);
        }
        if (closeFailures.Count != P.NoFailures || failures.Count != P.NoFailures)
        {
            storage.RetainRoot();
        }
        failures.AddRange(closeFailures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task ObserveSubmissionAsync(Task<OperationResult> submitted, Exception? expected, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await submitted.ConfigureAwait(false);
            }
            catch (Exception failure) when (ReferenceEquals(failure, expected))
            {
            }
        }, failures);
}
