using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueLifecycleConditionTrial
{
    internal static async Task Corruption(QueueLifecycleConditionFault fault)
    {
        using var fixture = new TestDatabase();
        var state = QueueLifecycleConditionOperations.Seed(fixture);
        await QueueLifecycleConditionAssertions.Ready(fixture.Database, fixture.Store, state);
        var cancel = QueueLifecycleConditionOperations.Cancel(fixture.Database, state);
        var originalCut = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        var original = QueueLifecycleConditionOperations.Damage(fixture.Store, state, fault);
        await QueueLifecycleConditionAssertions.Corrupt(fixture.Database, fixture.Store, state, cancel);
        await QueueLifecycleConditionAssertions.Corrupt(fixture.Database, fixture.Store, state, cancel);
        var damaged = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(originalCut.Position + 1);
        fixture.Store.Dispose();
        QueueLifecycleConditionCut healthy;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueueLifecycleConditionAssertions.Same(cold, damaged);
            await QueueLifecycleConditionAssertions.Corrupt(owner, cold, state, cancel);
            QueueLifecycleConditionOperations.Restore(cold, QueueLifecycleConditionOperations.Key(state, fault), original);
            await QueueLifecycleConditionAssertions.Same(cold, originalCut with
            {
                Position = originalCut.Position + 2
            });
            var result = owner.Apply(cancel);
            result.Get<CommitReceipt>();
            state.Successes.Add((cancel, result));
            await QueueLifecycleConditionAssertions.Replay(owner, cold, cancel, result);
            await QueueLifecycleConditionOperations.HealthyWork(owner, state);
            await QueueLifecycleConditionAssertions.Terminal(owner, cold, state);
            healthy = QueueLifecycleConditionAssertions.Capture(cold);
        }
        await SecondCold(fixture, state, healthy);
    }
    internal static async Task Refusal(QueueLifecycleConditionRefusal kind)
    {
        using var fixture = new TestDatabase();
        var state = QueueLifecycleConditionOperations.Seed(fixture);
        await QueueLifecycleConditionAssertions.Ready(fixture.Database, fixture.Store, state);
        if (kind == QueueLifecycleConditionRefusal.Cancelled)
        {
            var initial = QueueLifecycleConditionOperations.Cancel(fixture.Database, state);
            var cancelled = fixture.Database.Apply(initial);
            cancelled.Get<CommitReceipt>();
            state.Successes.Add((initial, cancelled));
        }
        var bad = kind switch
        {
            QueueLifecycleConditionRefusal.Version => QueueLifecycleConditionOperations.Cancel(fixture.Database, state, version: 0),
            QueueLifecycleConditionRefusal.Generation => QueueLifecycleConditionOperations.Cancel(fixture.Database, state, generation: 0),
            QueueLifecycleConditionRefusal.Missing => QueueLifecycleConditionOperations.Cancel(fixture.Database, state, id: "condition-missing"),
            QueueLifecycleConditionRefusal.Cancelled => QueueLifecycleConditionOperations.Cancel(fixture.Database, state, version: 2, generation: 2),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var expected = kind is QueueLifecycleConditionRefusal.Version or QueueLifecycleConditionRefusal.Generation
            ? ErrorCode.Validation : kind == QueueLifecycleConditionRefusal.Missing ? ErrorCode.NotFound : ErrorCode.Conflict;
        var queue = QueueLifecycleImage.Capture(fixture.Store, state.Lane);
        var failed = fixture.Database.Apply(bad);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(expected);

        await Assert.That(failed.Value).IsNull();
        state.Failures.Add((bad, failed));
        await QueueLifecycleImage.SameAsync(fixture.Store, state.Lane, queue);
        await QueueLifecycleConditionAssertions.Replay(fixture.Database, fixture.Store, bad, failed);
        var initialCut = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        fixture.Store.Dispose();
        QueueLifecycleConditionCut healthy;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = fixture.CreateConfiguredDatabase(cold);
            await QueueLifecycleConditionAssertions.Same(cold, initialCut);
            await QueueLifecycleConditionAssertions.Replay(owner, cold, bad, failed);
            if (kind != QueueLifecycleConditionRefusal.Cancelled)
            {
                var cancel = QueueLifecycleConditionOperations.Cancel(owner, state);
                var result = owner.Apply(cancel);
                result.Get<CommitReceipt>();
                state.Successes.Add((cancel, result));
            }
            await QueueLifecycleConditionOperations.HealthyWork(owner, state);
            await QueueLifecycleConditionAssertions.Terminal(owner, cold, state);
            healthy = QueueLifecycleConditionAssertions.Capture(cold);
        }
        await SecondCold(fixture, state, healthy);
    }
    private static async Task SecondCold(TestDatabase fixture, QueueLifecycleTestState state, QueueLifecycleConditionCut healthy)
    {
        using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var owner = fixture.CreateConfiguredDatabase(cold);
        await QueueLifecycleConditionAssertions.Same(cold, healthy);
        await QueueLifecycleConditionAssertions.Terminal(owner, cold, state);
        await QueueLifecycleOperations.ReplayAsync(owner, state);
        await QueueLifecycleConditionAssertions.Same(cold, healthy);
        await QueueLifecycleConditionAssertions.Terminal(owner, cold, state);
    }
}
