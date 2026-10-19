using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-raw-read-policy-public")]
[NotInParallel]
internal sealed class TextRawReadPolicyPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task CurrentRawReadGrantAdditionAndRemovalChangeOnlyLiteralProtectedProjectionOnOriginalSessions()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => TextRawReadPolicyPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
