namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRetentionAppendTests
{
    [Test]
    public async Task AcSeries015LateNewIdRejectsWholeAppendButRetainedIdDeduplicatesAndChangedContentConflicts()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var start = SampleAggregateTestData.Start;
        var floor = start.AddSeconds(2);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, start, 1),
            SampleAggregateTestData.Data(2, floor, 2));
        db.Commit(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor, 1));
        var lateBatch = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new AppendSamples(
            SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleAggregateTestData.Data(4, floor, 4), SampleAggregateTestData.Data(3, floor.AddTicks(-1), 3)])));
        var duplicate = db.Commit(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleAggregateTestData.Data(1, start, 1)]));
        var changedId = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new AppendSamples(
            SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleAggregateTestData.Data(1, start, 99)])));
        db.Commit(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleAggregateTestData.Data(5, floor.AddTicks(1), 5)]));
        var visible = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, start, floor.AddTicks(1));

        await Assert.That(lateBatch.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(duplicate.Mutations.Length).IsEqualTo(1);
        await Assert.That(changedId.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(visible.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { "sample-2", "sample-5" });
        await Assert.That(visible.Select(record => record.Sequence)).IsEquivalentTo(new long[] { 2, 3 });
    }

    [Test]
    public async Task AcSeries015EqualAndNewerSamplesRemainAppendableAfterRetention()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var floor = SampleAggregateTestData.Start.AddMinutes(1);
        db.Commit(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor));

        db.Commit(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleAggregateTestData.Data(1, floor, 1), SampleAggregateTestData.Data(2, floor.AddTicks(1), 2)]));
        var rows = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor, floor.AddTicks(1));

        await Assert.That(rows.Select(record => record.Sequence)).IsEquivalentTo(new long[] { 1, 2 });
    }
}
