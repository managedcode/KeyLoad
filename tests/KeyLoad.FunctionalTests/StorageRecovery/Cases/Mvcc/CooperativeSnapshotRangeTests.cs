using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class CooperativeSnapshotRangeTests
{
    private const byte RangePrefix = 0x80;
    private const byte Original = 1;
    private const byte Changed = 2;
    private const int Rows = 513;
    private static readonly byte[] Prefix = [RangePrefix];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SuspendedNativePageRetainsItsCutAndSettlesOnCancellation(bool cancel)
    {
        using var files = new CurrentStorageFixture();
        using var store = new ZoneTreeStore(files.SourceOptions, UnitExecutionOptions.StorageExecution());
        var cut = store.Commit((transaction, position) =>
        {
            for (var index = 0; index < Rows; index++)
            {
                transaction.Put(RowKey(index), [Original]);
            }
            return position;
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var context = new FirstYieldContext();
        var visited = new List<byte[]>();
        var before = store.GetReadDiagnostics();
        var read = context.Start(() => store.ReadAsync((view, token) =>
            ((ICooperativeRangeReadView)view).VisitRangeAsync(Prefix, Rows, (key, value) =>
            {
                if (store.Position != cut || !value.SequenceEqual([Original]))
                {
                    throw new InvalidOperationException("The suspended native scan changed read cuts.");
                }
                visited.Add(key.ToArray());
                return true;
            }, null, token), cancellation.Token).AsTask());
        try
        {
            await Assert.That(read.IsCompleted).IsFalse();
            await Assert.That(visited.Count).IsEqualTo(ZoneTreeRangeReader.CooperativeSteps);
            store.Commit((transaction, _) =>
            {
                transaction.Delete(RowKey(Rows - 2));
                transaction.Put(RowKey(Rows - 1), [Changed]);
                transaction.Put(RowKey(Rows), [Changed]);
                return true;
            });
        }
        finally
        {
            await FinishAsync(context, read, cancellation, cancel);
        }
        var after = store.GetReadDiagnostics();
        await Assert.That(after.RangeVisitAttempts - before.RangeVisitAttempts).IsEqualTo(1L);
        var expected = Enumerable.Range(0, cancel ? ZoneTreeRangeReader.CooperativeSteps : Rows).Select(RowKey);
        await Assert.That(visited.Select(Convert.ToHexString)).IsEquivalentTo(expected.Select(Convert.ToHexString), CollectionOrdering.Matching);
        var fresh = store.Read(view => view.ReadOwnedValue(RowKey(Rows - 1)));
        await Assert.That(fresh).IsEquivalentTo(new byte[] { Changed });
    }

    private static async Task FinishAsync(FirstYieldContext context, Task<StorageScanResult> read,
        CancellationTokenSource cancellation, bool cancel)
    {
        if (cancel)
        {
            await cancellation.CancelAsync();
        }
        context.Resume();
        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await read);
            return;
        }
        var result = await read;
        await Assert.That(result).IsEqualTo(new StorageScanResult(Rows, false, false, Rows * 4L));
    }

    private static byte[] RowKey(int index) => [RangePrefix, (byte)(index >> 8), (byte)index];

    private sealed class FirstYieldContext : SynchronizationContext
    {
        private SendOrPostCallback? continuation;
        private object? continuationState;

        internal Task<StorageScanResult> Start(Func<Task<StorageScanResult>> start)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                return start();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (continuation is not null)
            {
                base.Post(callback, state);
                return;
            }
            continuation = callback;
            continuationState = state;
        }

        internal void Resume()
        {
            if (continuation is { } callback)
            {
                base.Post(callback, continuationState);
            }
        }
    }
}
