using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.RecoveryTests.TimeSeries;

internal sealed class ChunkMultiblockProcessRecoveryTests
{
    private const string RootPrefix = "keyload-execution-multiblock-chunk-recovery-";

    [Test]
    [Arguments(false, CommitStage.JournalFlushed)]
    [Arguments(true, CommitStage.JournalFlushed)]
    [Arguments(false, CommitStage.MutationApplied)]
    [Arguments(true, CommitStage.MutationApplied)]
    public async Task TwoBlocksRecoverOriginalInterruptedSealOrMergeThenHealthyAndTwoColdOwners(bool merge, CommitStage stage)
    {
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await SampleChunkProcessRecovery.RunMultiblockAsync(root, merge, stage, TestContext.Current!.Execution.CancellationToken);
    }
}
