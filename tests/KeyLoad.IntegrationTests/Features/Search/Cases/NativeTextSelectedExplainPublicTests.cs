using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-selected-explain-public")]
[NotInParallel]
internal sealed class NativeTextSelectedExplainPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task SelectedTextContributionsRemainLiteralAcrossDeletionRestoreReplayAndTwoColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextSelectedExplainPublicTrial.RunAsync(fixture, linked.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
