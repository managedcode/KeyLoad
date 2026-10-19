using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class TextOnlineProcessRecoveryTests
{
    [Test]
    [Arguments(TextFaultStage.CanonicalOnlinePublicationAcknowledged)]
    [Arguments(TextFaultStage.OnlineCatalogPendingFlushed)]
    public async Task ActualOnlinePublicationKillReopenRetainsFullOriginalOutcomeCorruptionRepairAndHealthyContinuation(
        TextFaultStage stage)
        => await TextOnlineProcessTrial.RunAsync(stage, TestContext.Current!.Execution.CancellationToken);
}
