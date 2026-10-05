using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleAggregateBudgetTests
{
    [Test]
    public async Task AcSeries011SampleCapRequiresChargedLookaheadAndNeverReturnsTruncatedAggregate()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 1),
            SampleAggregateTestData.Data(2, SampleAggregateTestData.Start.AddTicks(1), 2));
        var request = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            MaxSamples: SampleAggregateTestData.OneResult);
        var before = db.Store.GetReadDiagnostics();

        var failure = SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request), ErrorCode.BudgetExceeded);
        var after = db.Store.GetReadDiagnostics();
        var exact = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            request with { MaxSamples = SampleAggregateTestData.OneResult + 1 });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(1L);
        await Assert.That(exact.Count).IsEqualTo(2L);
    }

    [Test]
    public async Task AcSeries011InvalidOrOverServerSampleAndWindowCapsReturnBudgetExceeded()
    {
        using var db = new TestDatabase(new DatabaseLimits
        {
            MaxScanRecords = SampleAggregateTestData.OneResult,
            MaxResults = SampleAggregateTestData.OneResult
        });
        SampleAggregateTestData.Configure(db);
        var aggregate = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        var windows = new AggregateSampleWindowsRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute), SampleAggregateTestData.Minute);

        var zeroSamples = SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, aggregate with { MaxSamples = SampleAggregateTestData.ZeroScanRecords }),
            ErrorCode.BudgetExceeded);
        var overSamples = SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, aggregate with { MaxSamples = SampleAggregateTestData.OneResult + 1 }),
            ErrorCode.BudgetExceeded);
        var zeroWindows = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, windows with { MaxWindows = SampleAggregateTestData.ZeroResults }),
            ErrorCode.BudgetExceeded);
        var overWindows = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, windows with { MaxWindows = SampleAggregateTestData.OneResult + 1 }),
            ErrorCode.BudgetExceeded);

        await Assert.That(zeroSamples.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(overSamples.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(zeroWindows.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(overWindows.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSeries011CompleteJsonOutputAcceptsExactBytesAndRejectsOneByteLess()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, -2));
        var request = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.AddTicks(1));
        var expected = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);
        var exactLength = JsonDefaults.Serialize(expected).Length;
        var exact = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = exactLength }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var oneByteShort = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = exactLength - SampleAggregateTestData.ExactBatchExtraByte }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());

        var actual = exact.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);
        var failure = SampleAggregateTestData.Failure(() => oneByteShort.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request), ErrorCode.BudgetExceeded);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSeries011CompleteRawReadBudgetAcceptsExactBytesAndRejectsOneByteLess()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 8));
        var request = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.AddTicks(1));
        var before = db.Store.GetReadDiagnostics();
        var expected = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);
        var after = db.Store.GetReadDiagnostics();
        var readBytes = after.PointExaminedBytes - before.PointExaminedBytes
            + after.RangeExaminedBytes - before.RangeExaminedBytes;
        var exact = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = readBytes }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var oneByteShort = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = readBytes - SampleAggregateTestData.ExactBatchExtraByte }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());

        var actual = exact.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);
        var failure = SampleAggregateTestData.Failure(() => oneByteShort.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request), ErrorCode.BudgetExceeded);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSeries011ReadByteBudgetPreCancellationAndElapsedDeadlineRejectAndPreserveStore()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var position = db.Store.Position;
        var request = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        var limited = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = SampleAggregateTestData.TinyReadBytes }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var readFailure = SampleAggregateTestData.Failure(() => limited.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request), ErrorCode.BudgetExceeded);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = Assert.ThrowsExactly<OperationCanceledException>(() => limited.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request, cancelled.Token));
        var expired = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { QueryDeadlineSeconds = SampleAggregateTestData.DeadlineSeconds }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var deadlineFailure = SampleAggregateTestData.Failure(() => expired.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request), ErrorCode.BudgetExceeded);
        var healthy = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);

        await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(cancellation).IsNotNull();
        await Assert.That(deadlineFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(healthy.Count).IsEqualTo(0L);
    }
}
