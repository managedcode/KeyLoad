using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class NativeTextIncrementalProcessRecoveryTests
{
    [Test]
    [Arguments(NativeTextFaultStage.NativePostingWritten)]
    [Arguments(NativeTextFaultStage.NativeInventoryFlushed)]
    [Arguments(NativeTextFaultStage.ManifestPublished)]
    [Arguments(NativeTextFaultStage.IncrementalIntentFlushed)]
    [Arguments(NativeTextFaultStage.NativeDeletionWritten)]
    [Arguments(NativeTextFaultStage.NativeAdditionWritten)]
    [Arguments(NativeTextFaultStage.CanonicalCheckpointAcknowledged)]
    [Arguments(NativeTextFaultStage.PendingIntentRetired)]
    public async Task AcFtsInc009DurableIncrementalReplaySurvivesRealPostingInventoryAndPublicationCuts(
        NativeTextFaultStage stage)
        => await NativeTextIncrementalProcessTrial.RunAsync(stage,
            TestContext.Current!.Execution.CancellationToken);
}
