using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class CapturedPolicyRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task OnlyChangesReadRevocationFencesCapturedAndFreshLiveReadsAcrossColdThenRepairPreservesOriginalReceipts()
    {
        using var deadline = McpCallerDeadline.Create();
        await CapturedPolicyRf3Flow.RunAsync(fixture, deadline.Token);
    }
}
