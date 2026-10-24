using KeyLoad.RecoveryTests.StorageRecovery;

namespace KeyLoad.RecoveryTests.Search;

internal sealed class AnnProcessRecoveryTests
{
    private const string Prefix = "keyload-ann-process-";
    private const string GuidFormat = "N";

    [Test]
    public async Task AcAnn007PendingPublicationAndJournalFlushedCheckpointRecoverArraysAndHealthyReplay()
    {
        var root = Path.Combine(AppContext.BaseDirectory, Prefix + Guid.NewGuid().ToString(GuidFormat));
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await AnnProcessRecovery.RunAsync(root, token);
    }
}
