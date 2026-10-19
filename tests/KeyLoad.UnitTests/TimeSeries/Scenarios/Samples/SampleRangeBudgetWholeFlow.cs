using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRangeBudgetWholeFlow
{
    internal static async Task RunAsync(SampleRangeBudgetFault fault)
    {
        using var fixture = new SampleRawIntegrityFixture();
        await SampleRawIntegrityAssertions.HealthyAsync(fixture, final: false);
        await SampleRangeBudgetAssertions.RefuseAsync(fixture, fault);
        var originalCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, originalCut);
        await SampleRangeBudgetAssertions.RefuseAsync(fixture, fault);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, fixture.SeedOperation, fixture.SeedReceipt);
        await SampleRangeBudgetAssertions.HealthyRowsAsync(fixture, appended: false);

        var append = fixture.Operation(new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            [SampleRawIntegrityScenario.Sample(5)], SampleAggregateTestData.EmptyTags));
        var previousPosition = fixture.Store.Position;
        var receipt = fixture.Apply(append);
        await SampleRangeBudgetAssertions.AppendReceiptAsync(fixture, append, receipt, previousPosition);
        await SampleRangeBudgetAssertions.HealthyRowsAsync(fixture, appended: true);
        await SampleRangeBudgetAssertions.RefuseAsync(fixture, fault);
        var healthyCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthyCut);
        await SampleRangeBudgetAssertions.HealthyRowsAsync(fixture, appended: true);
        await SampleRangeBudgetAssertions.RefuseAsync(fixture, fault);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, fixture.SeedOperation, fixture.SeedReceipt);
        await SampleRawIntegrityAssertions.ReplayAsync(fixture, append, receipt);
        await SampleRangeBudgetAssertions.HealthyRowsAsync(fixture, appended: true);
    }

    internal static DatabaseEngine BoundedOwner(SampleRawIntegrityFixture fixture, int maximumBytes)
        => new(fixture.Store, fixture.Owner.Authorization,
            UnitExecutionOptions.DatabaseLimits(new()
            {
                MaxBatchBytes = maximumBytes
            }),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.RuntimeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), timeProvider: fixture.Owner.EvaluationClock);

    internal static SampleRecord[] ReadPair(SampleRawIntegrityFixture fixture, DatabaseEngine owner,
        DateTimeOffset? from = null, DateTimeOffset? until = null, int? limit = null)
        => owner.ReadSamples(SampleAggregateTestData.RootPrincipal, fixture.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            from ?? SampleRawIntegrityScenario.InitialFloor, until ?? SampleRawIntegrityScenario.Sample(3).Timestamp,
            limit ?? owner.Limits.MaxResults);
}
