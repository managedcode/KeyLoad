namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationIdentityOutcomeTests
{
    [Test]
    public async Task ChangedRegisteredIncarnationRefusesDurablyThenOriginalGuidStaysConflictedAndFreshOwnerIdentityReplaysColdHealthy()
        => await PhysicalOwnerRegistrationIdentityOutcomeFlow.RunAsync();
}
