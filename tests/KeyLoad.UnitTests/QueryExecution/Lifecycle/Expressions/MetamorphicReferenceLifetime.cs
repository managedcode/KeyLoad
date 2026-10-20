using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class MetamorphicReferenceLifetime
{
    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long cut, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(SqlInnerJoinRetainedIdentityFlow.Image(store)
            .SequenceEqual(image, StringComparer.Ordinal)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(cut), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(OrleansSerialization.Serialize(store.Identity)
            .AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static Task ReplayAsync(MetamorphicReferenceFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => ObserveAsync(store, async () =>
        {
            var operation = database.CreateCommand(OperationKind.Batch, fixture.Seed.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(fixture.Seed, JsonDefaults.Options));
            var actual = database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
            await MetamorphicReferenceAssertions.EqualAsync(actual, fixture.Receipt);
            await MetamorphicReferenceFixture.RuntimeReceiptAsync(actual);
            var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Seed.Partition, "root", fixture.Seed.CommandId)))!;
            await Assert.That(bytes.AsSpan().SequenceEqual(fixture.Outcome)).IsTrue();
        });
    internal static async Task ColdAsync(MetamorphicReferenceFixture fixture, MetamorphicReferencePlan[] plans,
        bool descending, int offset, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = SqlInnerJoinRetainedIdentityFlow.Image(db.Store);
        var cut = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(db.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, db.StorageExecution);
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut, bytes), failures);
                var database = db.CreateConfiguredDatabase(store);
                await ServerFailureObserver.ObserveAsync(() => MetamorphicReferenceOperations.RunAsync(fixture,
                    database, store, plans, descending, offset, token), failures);
                await ServerFailureObserver.ObserveAsync(() => ReplayAsync(fixture, database, store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
