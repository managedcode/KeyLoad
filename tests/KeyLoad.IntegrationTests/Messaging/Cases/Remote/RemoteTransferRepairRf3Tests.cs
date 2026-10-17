namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferRepairRf3Tests
{
    [Test]
    [Arguments(QueueTransferRepairStage.Accept)]
    [Arguments(QueueTransferRepairStage.Complete)]
    public Task ActualKnownDeniedTransferStageRequiresPolicyRepairAndSourceCasBeforePublicHealthyAndTwoColdCuts(
        QueueTransferRepairStage stage) => RemoteTransferRepairRf3Trial.RunAsync(stage);
}
