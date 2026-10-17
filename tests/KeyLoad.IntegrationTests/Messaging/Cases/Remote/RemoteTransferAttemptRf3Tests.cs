namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferAttemptRf3Tests
{
    [Test]
    [Arguments(RemoteTransferAttemptRf3Protocol.ManualCeiling)]
    [Arguments(RemoteTransferAttemptRf3Protocol.AutomaticCeiling)]
    public Task GenuineCapacityRepairUsesBoundedCommittedAttemptOrOriginalManualRepairAcrossTwoColdCuts(int ceiling)
        => RemoteTransferAttemptRf3Trial.RunAsync(ceiling);
}
