using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-row-ownership-public")]
[NotInParallel]
internal sealed class TextRowOwnershipPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentRowOwnerConcealsExistingProtectedPostingsThenRepairAndOriginalReplaysPreserveLiteralPublicPages()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => TextRowOwnershipPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
