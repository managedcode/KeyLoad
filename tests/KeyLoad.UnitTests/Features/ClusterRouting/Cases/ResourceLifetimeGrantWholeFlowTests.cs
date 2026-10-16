namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ResourceLifetimeGrantWholeFlowTests
{
    [Test]
    public async Task AuthenticStoredLifetimeGrantRefusesSubstitutionThenCompletesAndReplaysAcrossTwoColdOwners()
        => await ResourceLifetimeGrantFlow.ExecuteWholeAsync();
}
