using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesCurrentCapabilityRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentSeriesReadCapabilityRevokeAndRestorePreserveLiteralPublicReadsAndOriginalReceiptAcrossTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await TimeSeriesCurrentCapabilityRf3Trial.RunAsync(fixture, linked.Token);
    }
}
