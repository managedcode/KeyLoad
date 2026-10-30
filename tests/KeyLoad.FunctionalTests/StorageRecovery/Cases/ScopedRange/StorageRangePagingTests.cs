using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>AC-QUERY-009: provider-sized pages preserve complete traversal and explicit reader stops.</summary>
internal sealed class StorageRangePagingTests
{
    private const int PageRecords = 3;
    private const int StoredRecords = 7;
    private const int RecordBytes = 2;

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task AcQuery009CompletePagesRespectThePhysicalRecordCeiling(int records)
    {
        using var fixture = new CurrentStorageFixture();
        using var store = Open(fixture, records);
        var visited = new List<byte>();
        var before = store.GetReadDiagnostics();
        var result = store.Read(view => view.VisitAll([], (key, _) =>
        {
            visited.Add(key[0]);
            return true;
        }));
        var after = store.GetReadDiagnostics();
        await Assert.That(visited.SequenceEqual(Enumerable.Range(0, records).Select(value => (byte)value))).IsTrue();
        await Assert.That(result.Records).IsEqualTo(records);
        await Assert.That(result.ReadBytes).IsEqualTo((long)records * RecordBytes);
        await Assert.That(result.HasMore).IsFalse();
        await Assert.That(result.StoppedByVisitor).IsFalse();
        await Assert.That(after.RangeBaselineEntries - before.RangeBaselineEntries).IsEqualTo((long)records);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(0L);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task AcQuery009ReaderStopAtOrBetweenPageBoundariesIsTerminal(int stopAfter)
    {
        using var fixture = new CurrentStorageFixture();
        using var store = Open(fixture, StoredRecords);
        var visited = new List<byte>();
        var result = store.Read(view => view.VisitAll([], (key, _) =>
        {
            visited.Add(key[0]);
            return visited.Count < stopAfter;
        }));
        await Assert.That(visited.SequenceEqual(Enumerable.Range(0, stopAfter).Select(value => (byte)value))).IsTrue();
        await Assert.That(result.Records).IsEqualTo(stopAfter);
        await Assert.That(result.ReadBytes).IsEqualTo((long)stopAfter * RecordBytes);
        await Assert.That(result.StoppedByVisitor).IsTrue();
        var healthy = store.Read(view => view.ScanAll([]));
        await Assert.That(healthy.Length).IsEqualTo(StoredRecords);
    }

    private static ZoneTreeStore Open(CurrentStorageFixture fixture, int records)
    {
        var store = new ZoneTreeStore(fixture.SourceOptions,
            UnitExecutionOptions.StorageExecution(new() { MaximumRangeRecords = PageRecords }),
            UnitExecutionOptions.PointCacheExecution());
        try
        {
            store.Commit((transaction, _) =>
            {
                for (var index = 0; index < records; index++)
                {
                    transaction.Put([(byte)index], [(byte)index]);
                }
                return true;
            });
            return store;
        }
        catch (Exception)
        {
            store.Dispose();
            throw;
        }
    }
}
