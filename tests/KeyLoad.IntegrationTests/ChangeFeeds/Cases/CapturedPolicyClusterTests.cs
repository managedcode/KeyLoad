using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CapturedPolicyClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyChangesReadRevocationFencesCapturedAndFreshLiveReadsAcrossColdThenRepairPreservesOriginalReceipts()
    {
        using var deadline = McpCallerDeadline.Create();
        await CapturedPolicyClusterFlow.RunAsync(fixture, deadline.Token);
    }
}
