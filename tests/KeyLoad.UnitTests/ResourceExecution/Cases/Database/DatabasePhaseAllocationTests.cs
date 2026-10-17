using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseAllocationTests
{
    private const int StartupBytesMaximum = 128 * 1024;
    private const int MeasuredRecords = 2_000;
    private const int HistogramCells = 32 * 6 * 16;
    private const int BusyCells = 32;
    private const int StripeCounterBytes = (HistogramCells + BusyCells) * sizeof(long);
    private const long ZeroStart = -1;

    [Test]
    [Arguments(1, 1)]
    [Arguments(1, 2)]
    [Arguments(1, 3)]
    [Arguments(1, 4)]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    [Arguments(2, 3)]
    [Arguments(2, 4)]
    [Arguments(4, 1)]
    [Arguments(4, 2)]
    [Arguments(4, 3)]
    [Arguments(4, 4)]
    public async Task BankStartupAndWarmEnabledRecordsStayWithinTheirAllocationBudgets(int stripeCount, int maximumCasAttempts)
    {
        var options = DatabasePhaseTestComposition.Bind(true, stripeCount, maximumCasAttempts);
        var captured = options.Value;
        var warmup = DatabasePhaseTestComposition.Create(options);
        WarmRecords(warmup);
        _ = warmup.Capture();
        GC.Collect();
        var constructionStart = GC.GetAllocatedBytesForCurrentThread();
        var bank = new DatabasePhaseBank(captured.Enabled, captured.StripeCount, captured.MaximumCasAttempts);
        var constructionBytes = GC.GetAllocatedBytesForCurrentThread() - constructionStart;

        WarmRecords(bank);
        _ = bank.Capture();
        var recordStart = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < MeasuredRecords; index++)
        {
            var started = bank.Begin();
            bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
            bank.RecordBusy(DatabasePhaseKind.NativeTreeMutation);
        }
        var recordBytes = GC.GetAllocatedBytesForCurrentThread() - recordStart;
        _ = bank.Capture();

        await Assert.That(constructionBytes).IsLessThanOrEqualTo(StartupBytesMaximum);
        await Assert.That(recordBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task LowerStripeReservationsReduceActualNativeArrayAllocation()
    {
        var maximumCasAttempts = DatabasePhaseTestComposition.Bind().Value.MaximumCasAttempts;
        var single = DatabasePhaseTestComposition.Bind(true, 1, maximumCasAttempts).Value;
        var paired = DatabasePhaseTestComposition.Bind(true, 2, maximumCasAttempts).Value;
        var maximum = DatabasePhaseTestComposition.Bind(true, 4, maximumCasAttempts).Value;
        _ = MeasureConstruction(maximum);
        var singleBytes = MeasureConstruction(single);
        var pairedBytes = MeasureConstruction(paired);
        var maximumBytes = MeasureConstruction(maximum);

        await Assert.That(singleBytes).IsGreaterThanOrEqualTo(StripeCounterBytes);
        await Assert.That(pairedBytes - singleBytes).IsGreaterThanOrEqualTo(StripeCounterBytes);
        await Assert.That(maximumBytes - pairedBytes).IsGreaterThanOrEqualTo(2L * StripeCounterBytes);
        await Assert.That(maximumBytes).IsLessThanOrEqualTo(StartupBytesMaximum);
    }

    [Test]
    public async Task DisabledBeginReturnsOnlyTheFrozenSentinel()
    {
        var bank = DatabasePhaseTestComposition.Create(false);
        var started = bank.Begin();
        bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, ZeroStart);
        var snapshot = bank.Capture();

        await Assert.That(started).IsEqualTo(ZeroStart);
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
        await Assert.That(snapshot.Histogram.IsDefaultOrEmpty).IsTrue();
    }

    private static long MeasureConstruction(DatabasePhaseExecutionOptions captured)
    {
        var started = GC.GetAllocatedBytesForCurrentThread();
        var bank = new DatabasePhaseBank(captured.Enabled, captured.StripeCount, captured.MaximumCasAttempts);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - started;
        GC.KeepAlive(bank);
        return allocated;
    }

    private static void WarmRecords(DatabasePhaseBank bank)
    {
        var started = bank.Begin();
        bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
        bank.RecordBusy(DatabasePhaseKind.NativeTreeMutation);
        _ = bank.Capture();
    }
}
