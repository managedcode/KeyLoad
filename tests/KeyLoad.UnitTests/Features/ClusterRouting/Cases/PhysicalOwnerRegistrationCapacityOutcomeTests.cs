namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationCapacityOutcomeTests
{
    [Test]
    public async Task FullDirectoryRefusesNewOwnerDurablyThenExistingOwnerFreshReceiptReplaysAcrossTwoColdCuts()
        => await PhysicalOwnerRegistrationCapacityOutcomeFlow.RunAsync();
}
