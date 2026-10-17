using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadOutboxFlow
{
    internal static async Task RunAsync(int rotation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new TestDatabase(limits: new()
            {
                MaxOutboxRecords = AtomicTriadOutboxFixture.Capacity
            });
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(source, rotation), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase source, int rotation)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = await SeedAsync(source, rotation, token);
        var original = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        source.JoinOwnedResources();
        var continued = await AtomicTriadOutboxCold.RunAsync(source, async (engine, store) =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(store))
                .IsEquivalentTo(original, CollectionOrdering.Matching);
            await AtomicTriadOutboxImage.AbsentAsync(engine, source.Partition);
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Seed, state.SeedResult, token);
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Failed, state.Refusal, token);
            var purged = engine.ApplyEmbedded(state.Purge, token);
            await AtomicTriadOutboxImage.ExactAsync(purged.Get<OutboxHead>(), new OutboxHead(1L, 2L, 0L, 0L));
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Failed, state.Refusal, token);
            await AtomicTriadOutboxImage.AbsentAsync(engine, source.Partition);
            var command = state.Command with
            {
                CommandId = Guid.NewGuid()
            };
            var healthy = AtomicTriadOutboxFixture.Operation(command);
            var result = engine.ApplyEmbedded(healthy, token);
            await AtomicTriadOutboxCommitted.RequireAsync(engine, store, command, result.Get<CommitReceipt>());
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, healthy, result, token);
            return new(command, healthy, result, purged, QueueWholeFlowStorage.Bytes(store), store.Position);
        });
        await AtomicTriadOutboxCold.RunAsync(source, async (engine, store) =>
        {
            await Assert.That(store.Position).IsEqualTo(continued.Position);
            await Assert.That(QueueWholeFlowStorage.Bytes(store))
                .IsEquivalentTo(continued.Image, CollectionOrdering.Matching);
            await AtomicTriadOutboxCommitted.RequireAsync(engine, store, continued.Command, continued.Result.Get<CommitReceipt>());
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Seed, state.SeedResult, token);
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Failed, state.Refusal, token);
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, state.Purge, continued.Purged, token);
            await AtomicTriadOutboxImage.ReplayAsync(engine, store, continued.Healthy, continued.Result, token);
            return continued;
        });
    }

    private static async Task<AtomicTriadOutboxState> SeedAsync(TestDatabase source, int rotation, CancellationToken token)
    {
        AtomicTriadOutboxFixture.Configure(source);
        var id = Guid.NewGuid();
        var seed = AtomicTriadOutboxFixture.Operation(new CommandRequest(id, source.Partition,
            [new PutDocument(AtomicTriadOutboxFixture.Collection, "seed", "{\"number\":1}", 0L)]));
        var seeded = source.Database.ApplyEmbedded(seed, token);
        seeded.Get<CommitReceipt>();
        var command = AtomicTriadOutboxFixture.Command(source.Partition, rotation);
        var operation = AtomicTriadOutboxFixture.Operation(command);
        var image = AtomicTriadOutboxImage.Domain(source.Store, source.Partition);
        var refusal = source.Database.ApplyEmbedded(operation, token);
        await AtomicTriadOutboxImage.ExactAsync(refusal,
            new OperationResult(null, ErrorCode.ResourceExhausted, AtomicTriadOutboxFixture.QuotaDetail));
        await Assert.That(AtomicTriadOutboxImage.Domain(source.Store, source.Partition))
            .IsEquivalentTo(image, CollectionOrdering.Matching);
        await AtomicTriadOutboxImage.AbsentAsync(source.Database, source.Partition);
        await AtomicTriadOutboxImage.ReplayAsync(source.Database, source.Store, operation, refusal, token);
        var purgeId = Guid.NewGuid();
        return new(command, operation, refusal, seed, seeded, AtomicTriadOutboxFixture.Operation(OperationKind.PurgeOutbox,
            purgeId, new PurgeOutboxRequest(purgeId, source.Partition, AtomicTriadOutboxFixture.SeedCount)));
    }
}
