using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleAggregateBudgetTests
{
    private const int CallerLifetimeMilliseconds = 1;
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
            request with
            {
                MaxSamples = SampleAggregateTestData.OneResult + 1
            });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(after.RangeLimitLookaheads - before.RangeLimitLookaheads).IsEqualTo(1L);
        await Assert.That(exact.Count).IsEqualTo(2L);
    }

    [Test]
    public async Task AcSeries011InvalidSampleAndWindowCapsReturnBudgetExceeded()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var limits = db.Database.Limits;
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, db.Database.EvaluationClock);
        var bounded = create(db.Store);
        var aggregate = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        var windows = new AggregateSampleWindowsRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute), SampleAggregateTestData.Minute,
            MaxSamples: SampleAggregateTestData.OneResult, MaxWindows: SampleAggregateTestData.OneResult);

        var zeroSamples = SampleAggregateTestData.Failure(() => bounded.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, aggregate with
            {
                MaxSamples = SampleAggregateTestData.ZeroScanRecords
            }),
            ErrorCode.BudgetExceeded);
        var zeroWindows = SampleAggregateTestData.Failure(() => bounded.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, windows with
            {
                MaxWindows = SampleAggregateTestData.ZeroResults
            }),
            ErrorCode.BudgetExceeded);

        await Assert.That(zeroSamples.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(zeroWindows.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSeries011PreCancellationAndElapsedCallerExpiryRejectAndPreserveStore()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var position = db.Store.Position;
        var request = new AggregateSamplesRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = Assert.ThrowsExactly<OperationCanceledException>(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RootPrincipal, request, cancelled.Token));
        var expiredBudget = new ReadExecutionBudget(TimeProvider.System);
        expiredBudget.ConstrainLifetime(TimeProvider.System.GetUtcNow().AddMilliseconds(CallerLifetimeMilliseconds));
        await Task.Delay(TimeSpan.FromMilliseconds(SampleAggregateTestData.DeadlineElapsedWaitMilliseconds), TimeProvider.System, TestContext.Current!.Execution.CancellationToken);
        var deadlineFailure = SampleAggregateTestData.Failure(() => db.Store.Read(view =>
            SampleAggregateReader.Read(db.Database, expiredBudget.CreateView(view), SampleAggregateTestData.RootPrincipal,
                request, expiredBudget)), ErrorCode.BudgetExceeded);
        var healthy = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal, request);

        await Assert.That(cancellation).IsNotNull();
        await Assert.That(deadlineFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(deadlineFailure.Message).IsEqualTo(SampleAggregateTestData.DeadlineExceededDetail);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(healthy.Count).IsEqualTo(0L);
    }
}
