using KeyLoad.RecoveryTests.Features.StorageRecovery;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class NativeAnnProcessRecoveryTests
{
    private const string Prefix = "keyload-ann-process-";
    private const string GuidFormat = "N";

    [Test]
    public async Task AcAnn007PendingPublicationAndJournalFlushedCheckpointRecoverNativeArraysAndHealthyReplay()
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await NativeAnnProcessRecovery.RunAsync(root, token);
    }
}
