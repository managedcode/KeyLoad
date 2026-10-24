using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueUnknownStateInspectionWholeFlow
{
    internal static async Task RunAsync(int persistedState, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => CompleteAsync(fixture, persistedState, failures, token), failures);
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

    private static async Task CompleteAsync(TestDatabase fixture, int persistedState,
        List<Exception> failures, CancellationToken token)
    {
        if (persistedState is not (-1 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(persistedState));
        }
        var state = QueueLifecycleConditionOperations.Seed(fixture);
        await QueueLifecycleConditionAssertions.Ready(fixture.Database, fixture.Store, state);
        var original = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.MetadataSpace, state.Partition,
            state.Lane.Queue, QueueLifecycleConditionOperations.Source);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var metadata = OrleansSerialization.Deserialize<MessageMetadata>(row);
        QueueLifecycleConditionOperations.Restore(fixture.Store, key,
            OrleansSerialization.Serialize(metadata with
            {
                State = (MessageState)persistedState
            }));
        var damaged = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await RefusalAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        QueueLifecycleConditionCut? healthy = null;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueLifecycleConditionAssertions.Same(cold, damaged);
                await RefusalAsync(owner, cold, state, damaged);
                QueueLifecycleConditionOperations.Restore(cold, key, row);
                await QueueLifecycleConditionAssertions.Same(cold, original with
                {
                    Position = original.Position + 2
                });
                await QueueLifecycleConditionAssertions.Ready(owner, cold, state);
                await QueueTerminalInspectionAssertions.ReplayAsync(owner, cold, state);
                await FinishAsync(owner, cold, state);
                healthy = QueueLifecycleConditionAssertions.Capture(cold);
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var restored = fixture.CreateConfiguredDatabase(second);
            await QueueLifecycleConditionAssertions.Same(second, healthy!);
            await QueueLifecycleConditionAssertions.Terminal(restored, second, state);
            await QueueTerminalInspectionAssertions.ReplayAsync(restored, second, state);
            await QueueLifecycleConditionAssertions.Same(second, healthy!);
            await QueueLifecycleConditionAssertions.Terminal(restored, second, state);
        }, failures);
    }

    private static async Task FinishAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var operation = QueueLifecycleConditionOperations.Cancel(owner, state);
        var result = owner.Apply(operation);
        result.Get<CommitReceipt>();
        state.Successes.Add((operation, result));
        await QueueLifecycleConditionOperations.HealthyWork(owner, state);
        await QueueLifecycleConditionAssertions.Terminal(owner, store, state);
        await QueueTerminalInspectionAssertions.ReplayAsync(owner, store, state);
    }

    private static async Task RefusalAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLifecycleTestState state, QueueLifecycleConditionCut cut)
    {
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(
                QueueLifecycleTestProtocol.Administrator, state.Lane, QueueLifecycleConditionOperations.Source));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo("The queue lifecycle metadata state is invalid.");
            await QueueLifecycleConditionAssertions.Same(store, cut);
        }
    }
}
