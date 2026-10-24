using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.EventStreams;

internal static class StreamRecordKeyAuthorityCold
{
    internal static async Task RunAsync(EventAppendWholeFlowFixture fixture, CommandRequest seed, Result<object> original, CommandRequest healthy, Result<object> healthyResult,
        StreamPage forward, StreamPage backward, CancellationToken token)
    {
        var position = fixture.Database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        fixture.Database.JoinOwnedResources();
        for (var cold = 0; cold < 2; cold++)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                using var store = new ZoneTreeStore(new(fixture.Database.Directory), fixture.Database.StorageExecution,
                    UnitExecutionOptions.PointCacheExecution(), timeProvider: fixture.Database.Database.EvaluationClock);
                var database = fixture.Database.CreateConfiguredDatabase(store);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(store.Position).IsEqualTo(position);
                    await Assert.That(QueueWholeFlowStorage.Bytes(store))
                        .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await StreamRecordKeyAuthorityAssertions.PagesFourAsync(database, fixture.Stream, fixture.Now, forward, backward, token);
                    await EventAppendSequenceAuthorityAssertions.ReplayAsync(database, store, seed, original, fixture.Now);
                    await EventAppendSequenceAuthorityAssertions.ReplayAsync(database, store, healthy, healthyResult, fixture.Now);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
