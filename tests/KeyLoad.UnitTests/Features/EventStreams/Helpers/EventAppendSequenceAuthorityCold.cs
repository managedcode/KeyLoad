using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventAppendSequenceAuthorityCold
{
    internal static async Task RunAsync(EventAppendWholeFlowFixture fixture, CommandRequest command,
        OperationResult original, CommandRequest healthy, OperationResult healthyResult, CancellationToken token)
    {
        var position = fixture.Database.Store.Position;
        var before = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        fixture.Database.JoinOwnedResources();
        for (var cold = 0; cold < 2; cold++)
        {
            await ColdAsync(fixture, command, original, healthy, healthyResult, position, before, token);
        }
    }

    private static async Task ColdAsync(EventAppendWholeFlowFixture fixture, CommandRequest command,
        OperationResult original, CommandRequest healthy, OperationResult healthyResult, long position,
        string[] before, CancellationToken token)
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
                    .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await EventAppendSequenceAuthorityAssertions.PageAsync(database, fixture.Stream, fixture.Now, token);
                await EventAppendSequenceAuthorityAssertions.ReplayAsync(database, store, command, original, fixture.Now);
                await EventAppendSequenceAuthorityAssertions.ReplayAsync(database, store, healthy, healthyResult, fixture.Now);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
