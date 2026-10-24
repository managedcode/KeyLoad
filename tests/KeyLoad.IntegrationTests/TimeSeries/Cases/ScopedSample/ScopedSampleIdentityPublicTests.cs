using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ScopedSampleIdentityPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task SiblingSeriesSampleIdsDeduplicateExactContentAndRejectAtomicValueOrTagConflictsAcrossTwoClusterColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await ScopedSampleIdentityPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
