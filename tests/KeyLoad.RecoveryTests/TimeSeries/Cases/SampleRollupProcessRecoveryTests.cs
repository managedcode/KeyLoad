using KeyLoad.RecoveryTests.StorageRecovery;

namespace KeyLoad.RecoveryTests.TimeSeries;

internal sealed class SampleRollupProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-rollup-process-";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcSeries022AcknowledgedAndJournalFlushedProcessCutsRetainRollupAuthority(bool drop)
    {
        var root = Path.Combine(AppContext.BaseDirectory, TrialPrefix + Guid.NewGuid().ToString("N"));
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await SampleRollupProcessRecovery.RunAsync(root, drop, token);
    }
}
