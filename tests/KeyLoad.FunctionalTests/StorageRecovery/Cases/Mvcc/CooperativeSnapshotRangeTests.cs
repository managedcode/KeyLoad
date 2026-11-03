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
    private const int FarRow = 1_024;
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
            VisitWithOrderedReadsAsync(store, (ICooperativeRangeReadView)view, cut, visited, token),
            cancellation.Token).AsTask());
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
        var expectedCount = cancel ? ZoneTreeRangeReader.CooperativeSteps : Rows;
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo((long)expectedCount);
        var expected = Enumerable.Range(0, expectedCount).Select(RowKey);
        await Assert.That(visited.Select(Convert.ToHexString)).IsEquivalentTo(expected.Select(Convert.ToHexString), CollectionOrdering.Matching);
        var fresh = store.Read(view => view.ReadOwnedValue(RowKey(Rows - 1)));
        await Assert.That(fresh).IsEquivalentTo(new byte[] { Changed });
    }

    [Test]
    public async Task OrderedPointsSeekAcrossSparseRangesWithoutChangingLogicalPointAccounting()
    {
        using var files = new CurrentStorageFixture();
        using var store = new ZoneTreeStore(files.SourceOptions, UnitExecutionOptions.StorageExecution(
            new() { MaximumRangeRecords = 1, MaximumRangeWorkBytes = 1 }));
        store.Commit((transaction, _) =>
        {
            for (var index = 0; index < Rows - 1; index++)
            {
                transaction.Put(RowKey(index), [Original]);
            }
            transaction.Put(RowKey(Rows), [Original]);
            transaction.Put(RowKey(FarRow), [Changed]);
            transaction.Put([RangePrefix + 1, 0, 0], [Changed]);
            return true;
        });
        var before = store.GetReadDiagnostics();
        var keys = new[] { 0, 1, Rows - 1, Rows, Rows + 1, FarRow, FarRow + 1, ushort.MaxValue };
        var results = store.Read(view =>
        {
            using var ordered = ((ICooperativeRangeReadView)view).OpenOrderedPointReader(Prefix,
                TestContext.Current!.Execution.CancellationToken);
            return keys.Select(key => Probe(ordered, key)).ToArray();
        });
        var after = store.GetReadDiagnostics();
        byte?[] expected = [Original, Original, null, Original, null, Changed, null, null];
        await Assert.That(results).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo((long)keys.Length);
        await Assert.That(after.PointExaminedBytes - before.PointExaminedBytes).IsEqualTo((long)keys.Length * 3 + 4);
        await Assert.That(after.RangeVisitAttempts).IsEqualTo(before.RangeVisitAttempts);
        await Assert.That(after.RangeBaselineEntries).IsEqualTo(before.RangeBaselineEntries);
    }

    private static async ValueTask<StorageScanResult> VisitWithOrderedReadsAsync(ZoneTreeStore store,
        ICooperativeRangeReadView view, long cut, List<byte[]> visited, CancellationToken token)
    {
        using var ordered = view.OpenOrderedPointReader(Prefix, token);
        return await view.VisitRangeAsync(Prefix, Rows, (key, value) =>
        {
            RequireOriginal(value);
            if (store.Position != cut || !ordered.Read(key[Prefix.Length..], RequireOriginal))
            {
                throw new InvalidOperationException("The suspended native scan changed read cuts.");
            }
            visited.Add(key.ToArray());
            return true;
        }, null, token);
    }

    private static void RequireOriginal(ReadOnlySpan<byte> value)
    {
        if (!value.SequenceEqual([Original]))
        {
            throw new InvalidOperationException("The suspended native scan changed a stored value.");
        }
    }

    private static byte? Probe(IOrderedPointReader reader, int key)
    {
        byte? value = null;
        _ = reader.Read(RowKey(key).AsSpan(Prefix.Length), bytes => value = bytes[0]);
        return value;
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
