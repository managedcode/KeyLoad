namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferAttemptClusterTests
{
    [Test]
    [Arguments(RemoteTransferAttemptClusterProtocol.ManualCeiling)]
    [Arguments(RemoteTransferAttemptClusterProtocol.AutomaticCeiling)]
    public Task GenuineCapacityRepairUsesBoundedCommittedAttemptOrOriginalManualRepairAcrossTwoColdCuts(int ceiling)
        => RemoteTransferAttemptClusterTrial.RunAsync(ceiling);
}
