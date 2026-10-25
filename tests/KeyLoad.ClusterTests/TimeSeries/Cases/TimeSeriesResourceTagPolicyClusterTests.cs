using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.TimeSeries;

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
