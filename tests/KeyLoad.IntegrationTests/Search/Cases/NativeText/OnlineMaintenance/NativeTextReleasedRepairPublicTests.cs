using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-released-repair-public")]
[NotInParallel]
internal sealed class NativeTextReleasedRepairPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task ReleasedSelectionStaysFencedWhileNewConsumerRestoresLiteralPagesAndOriginalResultsAcrossTwoColdCycles()
    {
        using var deadline = McpCallerDeadline.Create();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        await NativeTextReleasedRepairPublicTrial.RunAsync(fixture, linked.Token);
    }
}
