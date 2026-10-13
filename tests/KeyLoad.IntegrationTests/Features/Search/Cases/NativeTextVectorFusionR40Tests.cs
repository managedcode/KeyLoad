using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-vector-fusion-r40")]
[NotInParallel]
internal sealed class NativeTextVectorFusionR40Tests(ClusterFixture fixture)
{
    [Test]
    public async Task MaintainedTextAndExactVectorRanksUseFreshBranchAuthorityAcrossRepairReplayAndTwoColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextVectorFusionR40Trial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
