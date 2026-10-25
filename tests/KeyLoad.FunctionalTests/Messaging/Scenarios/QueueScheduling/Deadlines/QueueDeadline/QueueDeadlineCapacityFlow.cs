using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using static KeyLoad.FunctionalTests.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadlineCapacityFlow
{
    internal static async Task PositiveAsync(bool sweep, QueueDeadlineCapacityFault control, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var state = await QueueDeadlineCapacitySeed.RunAsync(fixture, QueueDeadlineKind.ExpireLease, sweep, false, control, token);
        await QueueDeadlineCapacityImage.RequireAsync(fixture.Database, state.Lane, [(state.Input, state.Original)],
            state.Counters.NextReadySequence, long.MaxValue);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueDeadlineCapacityContinuation.RunAsync(database, cold, fixture.Directory, state, token);
    }

    internal static async Task RunAsync(QueueDeadlineKind kind, bool sweep, bool jitter,
        QueueDeadlineCapacityFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueDeadlineCapacitySeed.RunAsync(fixture, kind, sweep, jitter, fault, token);
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        var beforePosition = fixture.Store.Position;
        var beforeCanonical = fixture.Database.LastApplied;
        var domain = QueueDeadlineCapacityRefusal.Domain(fixture.Store, state.Lane, state.Attempt);
        var result = fixture.Database.Apply(state.Attempt);
        await Assert.That(fixture.Store.Position).IsEqualTo(beforePosition + CommitPositionStep);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(beforeCanonical + CommitPositionStep);
        await Assert.That(OrleansSerialization.Serialize(fixture.Store.Identity)).IsEquivalentTo(identity,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(QueueDeadlineCapacityRefusal.Domain(fixture.Store, state.Lane, state.Attempt)).IsEquivalentTo(domain,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result).IsEqualTo(Result<object>.Fail(Errors.Problem(ErrorCode.ResourceExhausted, CapacityDetail)));
        state.Outcomes.Add((state.Attempt, result));
        await QueueDeadlineCapacityRefusal.RequireAsync(fixture.Database, fixture.Store, state, result, token);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueDeadlineCapacityRefusal.RequireAsync(database, cold, state, result, token);
        cold.Commit((transaction, _) =>
        {
            transaction.Put(state.FaultKey, state.OriginalBytes);
            return true;
        });
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, state.Original)], state.Counters.NextReadySequence);
        var repaired = QueueWholeFlowStorage.Bytes(cold);
        var repairedPosition = cold.Position;
        await Assert.That(database.Apply(state.Attempt)).IsEqualTo(result);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, repaired, repairedPosition, identity);
        await Assert.That(database.ResolveOutcome(state.Attempt)).IsEqualTo(result);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, repaired, repairedPosition, identity);
        await QueueDeadlineCapacityContinuation.RunAsync(database, cold, fixture.Directory, state, token);
    }
}
