using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ProjectionPublicColdTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedAdministratorProjectionEffectsEpochFencesReleasePurgeAndOriginalReceiptsSurviveTwoColdRf3ThenHealthyGenerationTwo()
    {
        using var deadline = McpCallerDeadline.Create();
        await ProjectionPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
