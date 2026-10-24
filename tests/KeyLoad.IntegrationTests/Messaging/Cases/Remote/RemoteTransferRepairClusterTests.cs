namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class RemoteTransferRepairClusterTests
{
    [Test]
    [Arguments(QueueTransferRepairStage.Accept)]
    [Arguments(QueueTransferRepairStage.Complete)]
    public Task ActualKnownDeniedTransferStageRequiresPolicyRepairAndSourceCasBeforePublicHealthyAndTwoColdCuts(
        QueueTransferRepairStage stage) => RemoteTransferRepairClusterTrial.RunAsync(stage);
}
