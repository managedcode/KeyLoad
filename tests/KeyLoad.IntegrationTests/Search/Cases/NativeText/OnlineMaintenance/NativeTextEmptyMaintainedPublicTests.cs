using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-empty-maintained-public")]
[NotInParallel]
internal sealed class NativeTextEmptyMaintainedPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task EmptyNativeBootstrapRetainsOriginalReceiptAcrossTwoColdCyclesThenFirstDocumentFencesSelection()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextEmptyMaintainedPublicTrial.RunAsync(fixture, linked.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
