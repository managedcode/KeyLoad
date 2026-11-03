using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class AsyncSnapshotStreamTests
{
    private const string Collection = "streamed";
    private const string Original = "{\"value\":1}";
    private const string Changed = "{\"value\":2}";

    [Test]
    public async Task RealRowsArriveBeforeTheScanFinishesAndKeepOneSnapshotAcrossConcurrentWrites()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit([.. new[] { "a", "b", "c" }.Select(id => (Mutation)new PutDocument(Collection, id, Original))]);
        var cut = db.Store.Position;
        var prefix = KeySpace.Partition("document", db.Partition, Collection);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = CqrsStream.Create<DocumentRecord, int>(writer => db.Store.ReadAsync(
            (view, token) => ReadRowsAsync(db.Store, view, prefix, writer, release.Task, ended, cut, token),
            writer.CancellationToken), timeout.Token);
        await using var reader = stream.GetAsyncEnumerator(timeout.Token);
        var chunks = new List<CqrsStreamChunk<DocumentRecord, int>>();
        var before = db.Store.GetReadDiagnostics();
        try
        {
            await Assert.That(await reader.MoveNextAsync()).IsTrue();
            chunks.Add(reader.Current);
            await Assert.That(await reader.MoveNextAsync()).IsTrue();
            chunks.Add(reader.Current);
            await Assert.That(reader.Current.TryGetProgress(out var first)).IsTrue();
            await Assert.That(first!.Reference.Id).IsEqualTo("a");
            await Assert.That(ended.Task.IsCompleted).IsFalse();
            await Assert.That(db.Store.GetReadDiagnostics().RangeBaselineEntries - before.RangeBaselineEntries)
                .IsLessThan(3);
            db.Commit(new PutDocument(Collection, "b", Changed, ExplicitReplacement: true),
                new DeleteDocument(Collection, "c"), new PutDocument(Collection, "d", Changed));
            release.SetResult();
            while (await reader.MoveNextAsync())
            {
                chunks.Add(reader.Current);
            }
            await Assert.That(chunks.ToStreamResult().Value).IsEqualTo(3);
            var rows = chunks.Where(chunk => chunk.Kind == CqrsStreamChunkKind.Progress)
                .Select(chunk => chunk.TryGetProgress(out var row) ? row! : throw new InvalidOperationException()).ToArray();
            await Assert.That(string.Join(',', rows.Select(row => row.Reference.Id))).IsEqualTo("a,b,c");
            await Assert.That(rows.All(row => row.Json == Original && row.Revision == 1)).IsTrue();
            await Assert.That(db.Database.GetDocument("root", db.ExpectedReference(new(db.Partition, Collection, "b")))!.Json).IsEqualTo(Changed);
            await Assert.That(db.Database.GetDocument("root", db.ExpectedReference(new(db.Partition, Collection, "c")))).IsNull();
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Test]
    public async Task EarlyConsumerDisposalJoinsTheNativeReadBeforeTwoColdOpens()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", Original));
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = CqrsStream.Create<DocumentRecord, int>(writer => db.Store.ReadAsync<int>(async (view, token) =>
        {
            try
            {
                await writer.StartedAsync();
                var page = view.Scan(KeySpace.Partition("document", db.Partition, Collection), 1);
                await writer.ProgressAsync(OrleansSerialization.Deserialize<DocumentRecord>(page.Records.Single().Value.Span));
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }
            finally
            {
                ended.TrySetResult();
            }
        }, writer.CancellationToken), timeout.Token);
        var reader = stream.GetAsyncEnumerator(timeout.Token);
        await using (reader)
        {
            await Assert.That(await reader.MoveNextAsync()).IsTrue();
            await Assert.That(await reader.MoveNextAsync()).IsTrue();
            await Assert.That(ended.Task.IsCompleted).IsFalse();
        }
        await Assert.That(ended.Task.IsCompletedSuccessfully).IsTrue();
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        db.JoinOwnedResources();
        for (var cold = 0; cold < 2; cold++)
        {
            using var store = ZoneTreeExistingStore.Open(new(db.Directory) { Incarnation = identity.Incarnation },
                identity.NodeId, db.StorageExecution);
            var engine = db.CreateConfiguredDatabase(store);
            await Assert.That(engine.GetDocument("root", db.ExpectedReference(new(db.Partition, Collection, "a")))!.Json).IsEqualTo(Original);
            await Assert.That(store.Position).IsEqualTo(cut);
        }
    }

    private static async ValueTask<int> ReadRowsAsync(ZoneTreeStore store, IKeyValueView view, byte[] prefix,
        ICqrsStreamWriter<DocumentRecord, int> writer, Task release, TaskCompletionSource ended, long cut,
        CancellationToken token)
    {
        try
        {
            await writer.StartedAsync();
            byte[]? after = null;
            var count = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = view.Scan(prefix, 1, after);
                if (page.Records.IsEmpty)
                {
                    return count;
                }
                await writer.ProgressAsync(OrleansSerialization.Deserialize<DocumentRecord>(page.Records.Single().Value.Span));
                count++;
                if (count == 1)
                {
                    await release.WaitAsync(token);
                }
                if (store.Position != cut)
                {
                    throw new InvalidOperationException("The read snapshot changed during result delivery.");
                }
                after = page.Records[^1].Key.ToArray();
            }
        }
        finally
        {
            ended.TrySetResult();
        }
    }
}
