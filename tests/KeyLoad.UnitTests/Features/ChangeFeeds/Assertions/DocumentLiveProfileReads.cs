using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileReads
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
        => GraphLabelUseOperations.ObserveAsync(store, operation);
    internal static async Task RefuseAsync(Func<object> operation, ErrorCode code, string detail)
    {
        object? result = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => result = operation());
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        await Assert.That(result).IsNull();
    }
    internal static async Task ModelsAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, ZoneTreeStore store,
        ModelQuerySourceKind kind, string cursor, CancellationToken token)
    {
        await ObserveAsync(store, async () =>
        {
            await RefuseAsync(() => queries.StartLiveQuery(DocumentLiveProfileFixture.Reader,
                new(fixture.Model(kind) with
                {
                    AstVersion = 0
                }), cancellationToken: token),
                ErrorCode.UnsupportedCapability, "The query AST version is unsupported.");
            await RefuseAsync(() => queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader,
                new(fixture.Model(kind) with
                {
                    AllowFullScan = false
                }, cursor), cancellationToken: token),
                ErrorCode.UnsupportedCapability, "A model query requires explicit full-scan consent.");
        });
        await ObserveAsync(store, async () =>
        {
            var before = store.GetReadDiagnostics();
            await RefuseAsync(() => queries.StartLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Model(kind)), cancellationToken: token),
                ErrorCode.UnsupportedCapability, DocumentLiveProfileFixture.Unsupported);
            await Assert.That(store.GetReadDiagnostics()).IsEqualTo(before);
        });
        await ObserveAsync(store, async () =>
        {
            var before = store.GetReadDiagnostics();
            await RefuseAsync(() => queries.ReadLiveQuery(DocumentLiveProfileFixture.Reader, new(fixture.Model(kind), cursor), cancellationToken: token),
                ErrorCode.UnsupportedCapability, DocumentLiveProfileFixture.Unsupported);
            await Assert.That(store.GetReadDiagnostics()).IsEqualTo(before);
        });
    }
    internal static Task CancelledAsync(DocumentLiveProfileFixture fixture, QueryEngine queries, ZoneTreeStore store, string cursor)
        => ObserveAsync(store, async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            object? result = null;
            var start = Assert.ThrowsExactly<OperationCanceledException>(() => result = queries.StartLiveQuery(
                DocumentLiveProfileFixture.Reader, new(fixture.Query), cancellationToken: cancellation.Token));
            await Assert.That(start.CancellationToken).IsEqualTo(cancellation.Token);
            var read = Assert.ThrowsExactly<OperationCanceledException>(() => result = queries.ReadLiveQuery(
                DocumentLiveProfileFixture.Reader, new(fixture.Query, cursor), cancellationToken: cancellation.Token));
            await Assert.That(read.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(result).IsNull();
        });
    internal static async Task ReplayAsync(DocumentLiveProfileFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => await ObserveAsync(store, async () =>
        {
            foreach (var command in fixture.Commands)
            {
                var replay = database.ApplyEmbedded(command.Operation, token).Get<CommitReceipt>();
                await EqualAsync(replay, command.Receipt);
                foreach (var effect in replay.Mutations)
                {
                    await Assert.That(effect.CompositionReferences).IsEmpty();
                }
                await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition,
                    "root", command.Operation.Id)))!.AsSpan().SequenceEqual(command.Outcome)).IsTrue();
            }
        });
}
