using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-request-refusal-public")]
[NotInParallel]
internal sealed class TextRequestRefusalPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task InvalidTextBranchAndZeroResultFusionLimitsPreserveLiteralHealthyPagesAndOriginalReceipts()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => TextRequestRefusalPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
