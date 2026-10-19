namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferRepairColdTests
{
    [Test]
    public Task DeniedAcceptAndCompleteRequireFreshPolicyRepairAndBoundedSourceCasAcrossTwoColdCuts()
        => RemoteTransferRepairColdTrial.RunAsync(RemoteTransferRepairColdProtocol.FullCeiling);

    [Test]
    public Task RepairCeilingRetainsOriginalDenialsThenExplicitOperatorCompleteSurvivesTwoColdCuts()
        => RemoteTransferRepairColdTrial.RunAsync(RemoteTransferRepairColdProtocol.ExhaustedCeiling);
}
