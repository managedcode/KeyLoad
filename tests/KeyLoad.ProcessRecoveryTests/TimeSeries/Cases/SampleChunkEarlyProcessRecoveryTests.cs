namespace KeyLoad.ProcessRecoveryTests.TimeSeries;

internal sealed class SampleChunkEarlyProcessRecoveryTests
{
    private const string RootPrefix = "keyload-execution-chunk-early-recovery-";
    [Test]
    [Arguments(SampleChunkEarlyCut.Open)]
    [Arguments(SampleChunkEarlyCut.Append)]
    [Arguments(SampleChunkEarlyCut.Correction)]
    public async Task AcChunk012JournalFlushedOpenAppendAndCorrectionRecoverCompleteLiteralThenColdHealthy(SampleChunkEarlyCut cut)
    {
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await SampleChunkProcessRecovery.RunEarlyAsync(root, cut, TestContext.Current!.Execution.CancellationToken);
    }
}
