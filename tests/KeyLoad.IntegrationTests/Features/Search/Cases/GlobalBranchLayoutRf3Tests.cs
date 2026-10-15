using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class GlobalBranchLayoutRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SameOriginalLogicalCorpusKeepsGlobalRrfAcrossPhysicalMoveMissingShardRepairPermissionsReceiptsAndTwoCold(bool official)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        await GlobalBranchLayoutRf3Flow.RunAsync(official, original.Token);
    }
}
