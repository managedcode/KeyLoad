using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphLabelUseOperations
{
    internal static async Task ReadAsync(DatabaseEngine database, ZoneTreeStore store, TestDatabase source,
        bool allow, CancellationToken token)
    {
        var partition = source.Partition;
        await ObserveAsync(store, async () =>
        {
            foreach (var labels in new string[]?[] { null, [] })
            {
                await EqualAsync(GraphLabelUseFixture.Expected(source, labels),
                    GraphLabelUseFixture.Traverse(database, partition, labels, token));
            }
        });
        foreach (var labels in new[] { new[] { "route" }, new[] { "unmatched" } })
        {
            await ObserveAsync(store, async () =>
            {
                if (allow)
                {
                    await EqualAsync(GraphLabelUseFixture.Expected(source, labels),
                        GraphLabelUseFixture.Traverse(database, partition, labels, token));
                }
                else
                {
                    await RefuseAsync(() => GraphLabelUseFixture.Traverse(database, partition, labels, token),
                        ErrorCode.PermissionDenied, GraphLabelUseFixture.DeniedDetail);
                }
            });
        }
        await ObserveAsync(store, () => RefuseAsync(() => GraphLabelUseFixture.Traverse(database, partition,
            ["route"], token, "missing-start"), allow ? ErrorCode.NotFound : ErrorCode.PermissionDenied,
            allow ? "The graph vertex is unavailable." : GraphLabelUseFixture.DeniedDetail));
    }

    internal static async Task RefuseAsync(Func<global::KeyLoad.GraphTraversal> read, ErrorCode code, string? detail)
    {
        global::KeyLoad.GraphTraversal? result = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result = read());
        await Assert.That(failure.Code).IsEqualTo(code);
        if (detail is not null)
        {
            await Assert.That(failure.Message).IsEqualTo(detail);
        }
        await Assert.That(result).IsNull();
    }

    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommandRequest seed, CommitReceipt receipt, byte[] outcome)
        => await ObserveAsync(store, async () =>
        {
            var operation = database.CreateCommand(OperationKind.Batch, seed.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options));
            var replay = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
            await EqualAsync(receipt, replay);
            foreach (var mutation in replay.Mutations)
            {
                await Assert.That(mutation.CompositionReferences).IsEmpty();
            }
            var current = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(seed.Partition, "root", seed.CommandId)))
                ?? throw new InvalidOperationException("The original graph outcome must remain available.");
            await Assert.That(current.AsSpan().SequenceEqual(outcome)).IsTrue();
        });
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
