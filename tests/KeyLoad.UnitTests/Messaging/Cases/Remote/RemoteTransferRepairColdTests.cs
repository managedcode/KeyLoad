namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferRepairColdTests
{
    [Test]
    public Task NativeDeniedAcceptAndCompleteRequireFreshPolicyRepairAndBoundedSourceCasAcrossTwoColdCuts()
        => RemoteTransferRepairColdTrial.RunAsync(RemoteTransferRepairColdProtocol.FullCeiling);

    [Test]
    public Task NativeRepairCeilingRetainsOriginalDenialsThenExplicitOperatorCompleteSurvivesTwoColdCuts()
        => RemoteTransferRepairColdTrial.RunAsync(RemoteTransferRepairColdProtocol.ExhaustedCeiling);
}
