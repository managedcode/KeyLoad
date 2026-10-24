using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ProjectionPublicColdTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedAdministratorProjectionEffectsEpochFencesReleasePurgeAndOriginalReceiptsSurviveTwoColdClusterThenHealthyGenerationTwo()
    {
        using var deadline = McpCallerDeadline.Create();
        await ProjectionPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
