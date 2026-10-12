namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationPolicyReplayTests
{
    [Test]
    public async Task SuccessfulOriginalRegistrationRefusesReplayAfterPolicyRevocationAndRegrantThenFreshOwnerReceiptSurvivesTwoColdCuts()
        => await PhysicalOwnerRegistrationPolicyReplayFlow.RunAsync();
}
