using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseAllocationTests
{
    private const int StartupBytesMaximum = 128 * 1024;
    private const int MeasuredRecords = 2_000;
    private const long ZeroStart = -1;

    [Test]
    public async Task BankStartupAndWarmEnabledRecordsStayWithinTheirAllocationBudgets()
    {
        var warmup = new DatabasePhaseBank(true);
        WarmRecords(warmup);
        _ = warmup.Capture();
        GC.Collect();
        var constructionStart = GC.GetAllocatedBytesForCurrentThread();
        var bank = new DatabasePhaseBank(true);
        var constructionBytes = GC.GetAllocatedBytesForCurrentThread() - constructionStart;

        WarmRecords(bank);
        _ = bank.Capture();
        var recordStart = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < MeasuredRecords; index++)
        {
            var started = bank.Begin();
            bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
        }
        var recordBytes = GC.GetAllocatedBytesForCurrentThread() - recordStart;
        _ = bank.Capture();

        await Assert.That(constructionBytes).IsLessThanOrEqualTo(StartupBytesMaximum);
        await Assert.That(recordBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task DisabledBeginReturnsOnlyTheFrozenSentinel()
    {
        var bank = new DatabasePhaseBank(false);
        var started = bank.Begin();
        bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, ZeroStart);
        var snapshot = bank.Capture();

        await Assert.That(started).IsEqualTo(ZeroStart);
        await Assert.That(snapshot.Quality).IsEqualTo(DatabaseProfileQuality.None);
        await Assert.That(snapshot.Histogram.IsDefaultOrEmpty).IsTrue();
    }

    private static void WarmRecords(DatabasePhaseBank bank)
    {
        var started = bank.Begin();
        bank.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
        _ = bank.Capture();
    }
}
