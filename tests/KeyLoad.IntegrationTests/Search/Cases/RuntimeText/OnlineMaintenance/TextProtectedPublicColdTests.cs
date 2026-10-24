using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-protected-public-cold")]
[NotInParallel]
internal sealed class TextProtectedPublicColdTests(ClusterFixture fixture)
{
    [Test]
    public async Task ProtectedPatchDeleteAndFieldUseRepairPreservePublicPagesAndOriginalReceiptsAcrossTwoClusterColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => TextProtectedPublicColdTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
