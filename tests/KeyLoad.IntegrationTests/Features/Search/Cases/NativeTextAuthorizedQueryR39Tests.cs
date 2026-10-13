using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-authorized-query-r39")]
[NotInParallel]
internal sealed class NativeTextAuthorizedQueryR39Tests(ClusterFixture fixture)
{
    [Test]
    public async Task MaintainedSelectionUsesFreshReaderRowAndFieldAuthorityAcrossRepairReplayAndTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await NativeTextAuthorizedQueryR39Trial.RunAsync(fixture, linked.Token);
    }
}
