using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ScopedSampleIdentityPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task SiblingSeriesSampleIdsDeduplicateExactContentAndRejectAtomicValueOrTagConflictsAcrossTwoRf3ColdCuts()
    {
        using var deadline = McpCallerDeadline.Create();
        await ScopedSampleIdentityPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
