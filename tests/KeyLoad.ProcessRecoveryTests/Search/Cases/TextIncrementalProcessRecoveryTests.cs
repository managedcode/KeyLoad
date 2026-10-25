using KeyLoad.Server.Search;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal sealed class TextIncrementalProcessRecoveryTests
{
    [Test]
    [Arguments(TextFaultStage.PostingWritten)]
    [Arguments(TextFaultStage.InventoryFlushed)]
    [Arguments(TextFaultStage.ManifestPublished)]
    [Arguments(TextFaultStage.IncrementalIntentFlushed)]
    [Arguments(TextFaultStage.DeletionWritten)]
    [Arguments(TextFaultStage.AdditionWritten)]
    [Arguments(TextFaultStage.CanonicalCheckpointAcknowledged)]
    [Arguments(TextFaultStage.PendingIntentRetired)]
    public async Task AcFtsInc009DurableIncrementalReplaySurvivesRealPostingInventoryAndPublicationCuts(
        TextFaultStage stage)
        => await TextIncrementalProcessTrial.RunAsync(stage,
            TestContext.Current!.Execution.CancellationToken);
}
