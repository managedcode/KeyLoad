using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class NativeTextOnlineProcessRecoveryTests
{
    [Test]
    [Arguments(NativeTextFaultStage.CanonicalOnlinePublicationAcknowledged)]
    [Arguments(NativeTextFaultStage.OnlineCatalogPendingFlushed)]
    public async Task ActualOnlinePublicationKillReopenRetainsFullOriginalOutcomeCorruptionRepairAndHealthyContinuation(
        NativeTextFaultStage stage)
        => await NativeTextOnlineProcessTrial.RunAsync(stage, TestContext.Current!.Execution.CancellationToken);
}
