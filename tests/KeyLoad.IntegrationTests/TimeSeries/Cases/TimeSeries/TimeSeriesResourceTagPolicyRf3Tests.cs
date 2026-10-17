using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesResourceTagPolicyRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentResourceTagPolicyChangesProjectAndRestoreSameReaderAcrossReplayAndTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        await TimeSeriesResourceTagPolicyRf3Trial.RunAsync(fixture, deadline.Token);
    }
}
