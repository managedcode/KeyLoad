namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal sealed class SampleChunkProcessRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcChunk012JournalFlushedSealAndMergeRecoverFullGenerationThenColdHealthy(bool merge)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-chunk-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await SampleChunkProcessRecovery.RunAsync(root, merge, TestContext.Current!.Execution.CancellationToken);
    }
}
