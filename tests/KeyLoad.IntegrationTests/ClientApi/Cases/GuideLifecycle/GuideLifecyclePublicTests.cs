namespace KeyLoad.IntegrationTests.ClientApi;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GuideLifecyclePublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task StaticGuideControlsPreserveCancellationAndCurrentCredentialAcrossRepairReplayAndTwoColdOwners()
    {
        using var deadline = McpCallerDeadline.Create();
        await GuideLifecyclePublicTrial.RunAsync(fixture, deadline.Token);
    }
}
