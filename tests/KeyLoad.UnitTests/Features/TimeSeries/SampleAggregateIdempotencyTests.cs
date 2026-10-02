using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleAggregateIdempotencyTests
{
    [Test]
    public async Task AcSeries009ChangedSampleIdContentConflictsWithoutMutationAndAllowsNextAppend()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 4));
        var beforeSamples = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        var original = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, Request(db));
        var conflict = SampleAggregateTestData.Failure(() => SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start.AddSeconds(1), 7)), ErrorCode.Conflict);
        var afterConflictSamples = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        var afterConflict = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, Request(db));
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(2, SampleAggregateTestData.Start.AddSeconds(2), -1));
        var afterAppend = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, Request(db));

        await Assert.That(conflict.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(beforeSamples.Length).IsEqualTo(1);
        await Assert.That(afterConflictSamples.SequenceEqual(beforeSamples)).IsTrue();
        await Assert.That(original.Count).IsEqualTo(1L);
        await Assert.That(original.Sum).IsEqualTo(4d);
        await Assert.That(afterConflict).IsEqualTo(original);
        await Assert.That(afterAppend.Count).IsEqualTo(2L);
        await Assert.That(afterAppend.Sum).IsEqualTo(3d);
        await Assert.That(afterAppend.Minimum).IsEqualTo(-1d);
        await Assert.That(afterAppend.Maximum).IsEqualTo(4d);
        await Assert.That(afterAppend.Average).IsEqualTo(1.5d);
    }

    private static AggregateSamplesRequest Request(TestDatabase database)
        => new(database.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            SampleAggregateTestData.Start, SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
}
