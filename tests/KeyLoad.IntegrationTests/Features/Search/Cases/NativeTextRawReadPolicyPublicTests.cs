using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-raw-read-policy-public")]
[NotInParallel]
internal sealed class NativeTextRawReadPolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentRawReadGrantAdditionAndRemovalChangeOnlyLiteralProtectedProjectionOnOriginalSessions()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextRawReadPolicyPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
