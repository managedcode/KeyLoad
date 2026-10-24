using KeyLoad.IntegrationTests.ClientApi;
namespace KeyLoad.IntegrationTests.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesResourceTagPolicyClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentResourceTagPolicyChangesProjectAndRestoreSameReaderAcrossReplayAndTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        await TimeSeriesResourceTagPolicyClusterTrial.RunAsync(fixture, deadline.Token);
    }
}
