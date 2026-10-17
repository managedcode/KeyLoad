using KeyLoad.RecoveryTests.Features.StorageRecovery;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal sealed class SampleRollupProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-rollup-process-";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcSeries022AcknowledgedAndJournalFlushedProcessCutsRetainRollupAuthority(bool drop)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await SampleRollupProcessRecovery.RunAsync(root, drop, token);
    }
}
