using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SeriesRetentionAtomicPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task RetentionKeepsPurgedIdentityAndMixedBatchRollbackThroughBothPublicClientsAndJoinedCold()
    {
        using var deadline = McpCallerDeadline.Create();
        await SeriesRetentionAtomicPublicTrial.RunAsync(fixture, deadline.Token);
    }
}
