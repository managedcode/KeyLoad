using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEmptyReceiveReplayWholeFlow
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
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(TestDatabase fixture, List<Exception> failures, CancellationToken token)
    {
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        await EmptyAsync(fixture.Database, fixture.Store, state);
        state.Batch(fixture.Database, QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Source)).Get<CommitReceipt>();
        await QueueLifecycleConditionAssertions.Ready(fixture.Database, fixture.Store, state);
        await QueueTerminalInspectionAssertions.ReplayAsync(fixture.Database, fixture.Store, state);
        var ready = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        QueueLifecycleConditionCut? healthy = null;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueLifecycleConditionAssertions.Same(cold, ready);
                await QueueLifecycleConditionAssertions.Ready(owner, cold, state);
                await QueueTerminalInspectionAssertions.ReplayAsync(owner, cold, state);
                await FinishAsync(owner, cold, state);
                healthy = QueueLifecycleConditionAssertions.Capture(cold);
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var healthyCut = healthy
            ?? throw new InvalidOperationException("The empty Receive cold continuation did not complete.");
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var owner = fixture.CreateConfiguredDatabase(second);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueTerminalInspectionAssertions.TerminalAsync(owner, second, state,
                QueueTerminalInspectionAssertions.Expected(MessageState.Acked, state), healthy: true);
            await QueueTerminalInspectionAssertions.ReplayAsync(owner, second, state);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
        }, failures);
    }

    private static async Task FinishAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var source = await QueueLifecycleOperations.ClaimAsync(owner, state, QueueLifecycleConditionOperations.Source);
        await QueueTerminalInspectionAssertions.DeliveryAsync(source, QueueLifecycleConditionOperations.Source, state.Time);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state, QueueLifecycleConditionOperations.Source);
        QueueLifecycleOperations.Complete(owner, state, source, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueTerminalInspectionAssertions.TerminalAsync(owner, store, state,
            QueueTerminalInspectionAssertions.Expected(MessageState.Acked, state), healthy: false);
        state.Batch(owner, QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Healthy)).Get<CommitReceipt>();
        await HealthyReadyAsync(owner, store, state);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state);
        var healthy = await QueueLifecycleOperations.ClaimAsync(owner, state, QueueLifecycleConditionOperations.Healthy);
        await QueueTerminalInspectionAssertions.DeliveryAsync(healthy, QueueLifecycleConditionOperations.Healthy, state.Time);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state, QueueLifecycleConditionOperations.Healthy);
        QueueLifecycleOperations.Complete(owner, state, healthy, DeliveryAction.Ack).Get<CommitReceipt>();
        await EmptyAsync(owner, store, state);
        await Assert.That(state.Successes.Count).IsEqualTo(8);
        await Assert.That(state.Failures.Count).IsEqualTo(0);
        await QueueTerminalInspectionAssertions.TerminalAsync(owner, store, state,
            QueueTerminalInspectionAssertions.Expected(MessageState.Acked, state), healthy: true);
    }

    private static async Task EmptyAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1,
            LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds);
        var result = state.Execute(owner, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
        await Assert.That(result.Deliveries).IsEmpty();
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state);
    }

    private static async Task HealthyReadyAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        var literal = QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Healthy);
        var body = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal));
        await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, literal.MessageId))
            .IsEqualTo(new MessageInspection(new(literal.MessageId, MessageState.Ready, 0, 1, 2, null, null),
                literal.PayloadJson, literal.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, literal.MessageId)))).IsEqualTo(body);
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue))))
            .IsEqualTo(new QueueCounters(1, NativeSerialization.Serialize(body).LongLength, 0, 0, 2));
        await QueueLifecycleConditionAssertions.Same(store, before);
    }
}
