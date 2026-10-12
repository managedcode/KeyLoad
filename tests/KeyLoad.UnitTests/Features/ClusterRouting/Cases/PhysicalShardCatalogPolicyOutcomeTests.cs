namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogPolicyOutcomeTests
{
    [Test]
    public Task AcScat001DeniedBootstrapKeepsOriginalOutcomeAfterGrantThenFreshIdentityReplaysAcrossColdOwners()
        => PhysicalShardCatalogPolicyOutcomeFlow.RunAsync();
}
