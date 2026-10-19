using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueTerminalInspectionWholeFlow
{
    internal static async Task RunAsync(MessageState terminalState, bool foreignBody, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => CompleteAsync(fixture, terminalState, foreignBody, failures, token), failures);
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

    private static async Task CompleteAsync(TestDatabase fixture, MessageState terminalState,
        bool foreignBody, List<Exception> failures, CancellationToken token)
    {
        var state = Seed(fixture, terminalState);
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.BodySpace, state.Partition,
            state.Lane.Queue, QueueLifecycleConditionOperations.Source);
        var body = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        await QueueTerminalInspectionAssertions.OriginalBodyAsync(body, terminalState, state);
        await TransitionAsync(fixture.Database, fixture.Store, state, terminalState);
        var expected = QueueTerminalInspectionAssertions.Expected(terminalState, state);
        await QueueTerminalInspectionAssertions.TerminalAsync(fixture.Database, fixture.Store, state, expected, false);
        await QueueTerminalInspectionAssertions.ReplayAsync(fixture.Database, fixture.Store, state);
        var original = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        var injected = foreignBody
            ? OrleansSerialization.Serialize(OrleansSerialization.Deserialize<MessageBody>(body) with
            {
                Id = "foreign-terminal-body"
            })
            : body;
        QueueLifecycleConditionOperations.Restore(fixture.Store, key, injected);
        var damaged = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await QueueTerminalInspectionAssertions.RefusalAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        QueueLifecycleConditionCut? healthy = null;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueLifecycleConditionAssertions.Same(cold, damaged);
                await QueueTerminalInspectionAssertions.RefusalAsync(owner, cold, state, damaged);
                cold.Commit((tx, _) => { tx.Delete(key); return true; });
                await QueueLifecycleConditionAssertions.Same(cold, original with
                {
                    Position = original.Position + 2
                });
                await QueueTerminalInspectionAssertions.TerminalAsync(owner, cold, state, expected, false);
                await QueueTerminalInspectionAssertions.ReplayAsync(owner, cold, state);
                await HealthyAsync(owner, state);
                await QueueTerminalInspectionAssertions.ReplayAsync(owner, cold, state);
                await QueueTerminalInspectionAssertions.TerminalAsync(owner, cold, state, expected, true);
                healthy = QueueLifecycleConditionAssertions.Capture(cold);
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var healthyCut = healthy
            ?? throw new InvalidOperationException("The terminal inspection cold continuation did not complete.");
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var restored = fixture.CreateConfiguredDatabase(second);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueTerminalInspectionAssertions.TerminalAsync(restored, second, state, expected, true);
            await QueueTerminalInspectionAssertions.ReplayAsync(restored, second, state);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueTerminalInspectionAssertions.TerminalAsync(restored, second, state, expected, true);
        }, failures);
    }

    private static QueueLifecycleTestState Seed(TestDatabase fixture, MessageState terminalState)
    {
        if (terminalState is not (MessageState.Acked or MessageState.Cancelled or MessageState.Expired))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalState));
        }
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var state = new QueueLifecycleTestState(fixture.Partition, time.AddSeconds(2));
        var id = Guid.NewGuid();
        var message = QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Source) with
        {
            ExpiresAt = terminalState == MessageState.Expired ? time.AddSeconds(1) : null
        };
        var command = new CommandRequest(id, state.Partition, [message]);
        var operation = fixture.Database.CreateCommand(OperationKind.Batch, id, QueueLifecycleTestProtocol.Administrator, time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var result = fixture.Database.Apply(operation);
        result.Get<CommitReceipt>();
        state.Successes.Add((operation, result));
        return state;
    }

    private static async Task TransitionAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLifecycleTestState state, MessageState terminalState)
    {
        if (terminalState == MessageState.Cancelled)
        {
            var operation = QueueLifecycleConditionOperations.Cancel(owner, state);
            var result = owner.Apply(operation);
            result.Get<CommitReceipt>();
            state.Successes.Add((operation, result));
            return;
        }
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds);
        var resultReceived = state.Execute(owner, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
        if (terminalState == MessageState.Expired)
        {
            await Assert.That(resultReceived.Deliveries).IsEmpty();
            return;
        }
        var delivery = await Assert.That(resultReceived.Deliveries).HasSingleItem();
        await QueueTerminalInspectionAssertions.DeliveryAsync(delivery, QueueLifecycleConditionOperations.Source, state.Time);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state, QueueLifecycleConditionOperations.Source);
        QueueLifecycleOperations.Complete(owner, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
    }

    private static async Task HealthyAsync(DatabaseEngine owner, QueueLifecycleTestState state)
    {
        state.Batch(owner, QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Healthy)).Get<CommitReceipt>();
        var delivery = await QueueLifecycleOperations.ClaimAsync(owner, state, QueueLifecycleConditionOperations.Healthy);
        await QueueTerminalInspectionAssertions.DeliveryAsync(delivery, QueueLifecycleConditionOperations.Healthy, state.Time);
        QueueLifecycleOperations.Complete(owner, state, delivery, DeliveryAction.Ack).Get<CommitReceipt>();
    }
}
