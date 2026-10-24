using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadLetterSelectedTrial
{
    internal static async Task RunAsync(QueueDeadLetterSelectedOperation kind, QueueDeadLetterSelectedFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueDeadLetterSelectedSetup.SeedAsync(fixture, kind, fault);
        var originalImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalPosition = fixture.Store.Position;
        QueueDeadLetterSelectedSetup.Damage(fixture.Store, state);
        await Assert.That(fixture.Store.Position).IsEqualTo(originalPosition + QueueDeadLetterSelectedProtocol.One);
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueDeadLetterSelectedRefusal.RequireAsync(fixture.Database, fixture.Store, state, damaged);
        await QueueDeadLetterSelectedRefusal.RequireAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await Assert.That(reopened.Position).IsEqualTo(originalPosition + QueueDeadLetterSelectedProtocol.One);
        await QueueDeadLetterSelectedRefusal.RequireAsync(database, reopened, state, damaged);
        reopened.Commit((tx, _) =>
        {
            foreach (var repair in state.Repairs)
            {
                tx.Put(repair.Key, repair.Bytes);
            }
            return true;
        });
        await Assert.That(reopened.Position).IsEqualTo(originalPosition + QueueDeadLetterSelectedProtocol.Two);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(originalImage, CollectionOrdering.Matching);
        await QueueDeadLetterSelectedImage.RequireAsync(database, reopened, state);
        await QueueDeadLetterSelectedHistory.ReplayAsync(database, reopened, state, state.History);
        var continuation = await QueueDeadLetterSelectedContinuation.RunAsync(database, reopened, state);
        var history = state.History.Concat(continuation).ToArray();
        await QueueDeadLetterSelectedHistory.ReplayAsync(database, reopened, state, history);
        var completed = QueueWholeFlowStorage.Bytes(reopened);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await Assert.That(QueueWholeFlowStorage.Bytes(cold)).IsEquivalentTo(completed, CollectionOrdering.Matching);
        await QueueDeadLetterSelectedHistory.ReplayAsync(restored, cold, state, history);
        await QueueDeadLetterSelectedImage.RequireAsync(restored, cold, state, settled: true, healthy: true);
        await QueueDeadLetterSelectedContinuation.ColdEffectsAsync(restored, cold, continuation, state.DocumentLifetime);
        await QueueDeadLetterSelectedRetainedInbox.RequireAsync(restored, cold, state);
        await Assert.That(QueueWholeFlowStorage.Bytes(cold)).IsEquivalentTo(completed, CollectionOrdering.Matching);
    }
}
