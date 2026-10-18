using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-protected-public-cold")]
[NotInParallel]
internal sealed class NativeTextProtectedPublicColdTests(ClusterFixture fixture)
{
    [Test]
    public async Task ProtectedPatchDeleteAndFieldUseRepairPreservePublicPagesAndOriginalReceiptsAcrossTwoClusterColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextProtectedPublicColdTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
