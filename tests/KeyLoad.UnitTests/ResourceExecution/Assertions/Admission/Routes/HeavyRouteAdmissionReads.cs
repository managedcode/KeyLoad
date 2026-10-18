using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class HeavyRouteAdmissionReads
{
    internal static PrincipalRecord Authenticate(DatabaseEngine database, ZoneTreeStore store)
    {
        var now = database.EvaluationClock.GetUtcNow();
        var subject = database.Authenticate(HeavyRouteAdmissionFixture.Secret, now);
        return store.Read(view => database.Principal(view, subject, now));
    }
    internal static Task PathAsync(HeavyRouteAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        bool use, CancellationToken token)
        => ObserveAsync(store, async () =>
        {
            var principal = Authenticate(database, store);
            if (!use)
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.ShortestPath(principal.Id, fixture.Path, cancellationToken: token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(failure.Message).IsEqualTo("The query requires a protected field-use grant.");
                return;
            }
            var expected = new GraphShortestPathResult(1, true, 1, [fixture.Vertex("a"), fixture.Vertex("b")], [fixture.Edge], store.Position);
            await EqualAsync(database.ShortestPath(principal.Id, fixture.Path, cancellationToken: token), expected);
        });
    internal static Task ReplayAsync(HeavyRouteAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => ObserveAsync(store, async () =>
        {
            var operation = database.CreateCommand(OperationKind.Batch, fixture.Seed.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(fixture.Seed, JsonDefaults.Options));
            var replay = database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
            await EqualAsync(replay, fixture.Receipt);
            await HeavyRouteAdmissionFixture.NativeReceiptAsync(replay);
            var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Seed.Partition, "root", fixture.Seed.CommandId)))!;
            await Assert.That(bytes.AsSpan().SequenceEqual(fixture.Outcome)).IsTrue();
        });
    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var cut = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long cut, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(
            SqlInnerJoinRetainedIdentityFlow.Image(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(cut), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(NativeSerialization.Serialize(store.Identity)
            .AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
