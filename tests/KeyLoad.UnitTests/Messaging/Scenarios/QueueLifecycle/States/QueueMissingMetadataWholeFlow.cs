using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueMissingMetadataWholeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => CompleteAsync(fixture, failures, token), failures);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(TestDatabase fixture, List<Exception> failures, CancellationToken token)
    {
        var state = QueueLifecycleConditionOperations.Seed(fixture);
        await QueueLifecycleConditionAssertions.Ready(fixture.Database, fixture.Store, state);
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.MetadataSpace, state.Partition,
            state.Lane.Queue, QueueLifecycleConditionOperations.Source);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        await Assert.That(row.SequenceEqual(OrleansSerialization.Serialize(new MessageMetadata(
            QueueLifecycleConditionOperations.Source, MessageState.Ready, 0, 1, 1, null, null)))).IsTrue();
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1, LeaseSeconds: 30);
        var operation = fixture.Database.CreateCommand(OperationKind.Receive, request.RequestId, QueueLifecycleTestProtocol.Administrator, state.Time, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var original = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        fixture.Store.Commit((tx, _) => { tx.Delete(key); return true; });
        var damaged = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await QueueMissingMetadataAssertions.RefusalAsync(fixture.Database, fixture.Store, state, operation, damaged);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        QueueLifecycleConditionCut? healthy = null;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueLifecycleConditionAssertions.Same(cold, damaged);
                await QueueMissingMetadataAssertions.RefusalAsync(owner, cold, state, operation, damaged);
                QueueLifecycleConditionOperations.Restore(cold, key, row);
                await QueueLifecycleConditionAssertions.Same(cold, original with
                {
                    Position = original.Position + 2
                });
                await QueueLifecycleConditionAssertions.Ready(owner, cold, state);
                await QueueTerminalInspectionAssertions.ReplayAsync(owner, cold, state);
                await FinishAsync(owner, cold, state, operation);
                healthy = QueueLifecycleConditionAssertions.Capture(cold);
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var healthyCut = healthy
            ?? throw new InvalidOperationException("The metadata repair cold continuation did not complete.");
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var owner = fixture.CreateConfiguredDatabase(second);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueMissingMetadataAssertions.TerminalAsync(owner, second, state);
            await QueueTerminalInspectionAssertions.ReplayAsync(owner, second, state);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueMissingMetadataAssertions.TerminalAsync(owner, second, state);
        }, failures);
    }

    private static async Task FinishAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLifecycleTestState state, Command<DatabaseCommand> operation)
    {
        var result = owner.Apply(operation);
        var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueTerminalInspectionAssertions.DeliveryAsync(delivery, QueueLifecycleConditionOperations.Source, state.Time);
        state.Successes.Add((operation, result));
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state, QueueLifecycleConditionOperations.Source);
        QueueLifecycleOperations.Complete(owner, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
        state.Batch(owner, QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Healthy)).Get<CommitReceipt>();
        var healthy = await QueueLifecycleOperations.ClaimAsync(owner, state, QueueLifecycleConditionOperations.Healthy);
        await QueueTerminalInspectionAssertions.DeliveryAsync(healthy, QueueLifecycleConditionOperations.Healthy, state.Time);
        QueueLifecycleOperations.Complete(owner, state, healthy, DeliveryAction.Ack).Get<CommitReceipt>();
        await Assert.That(state.Successes.Count).IsEqualTo(6);
        await Assert.That(state.Failures.Count).IsEqualTo(0);
        await QueueMissingMetadataAssertions.TerminalAsync(owner, store, state);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state);
    }
}
