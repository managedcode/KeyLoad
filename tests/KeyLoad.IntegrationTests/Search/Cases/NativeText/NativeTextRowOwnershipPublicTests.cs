using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-row-ownership-public")]
[NotInParallel]
internal sealed class NativeTextRowOwnershipPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentRowOwnerConcealsExistingProtectedPostingsThenRepairAndOriginalReplaysPreserveLiteralPublicPages()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextRowOwnershipPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
