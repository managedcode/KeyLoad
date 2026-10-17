using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SparseCompositePrefixRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SparseCompositePrefixesKeepAllPublicAdapterRowsFreshUseReceiptsAndTwoCold(bool includeNull, bool includeMissing)
    {
        using var deadline = McpCallerDeadline.Create();
        await SparseCompositePrefixRf3Flow.RunAsync(fixture, includeNull, includeMissing, deadline.Token);
    }
}
