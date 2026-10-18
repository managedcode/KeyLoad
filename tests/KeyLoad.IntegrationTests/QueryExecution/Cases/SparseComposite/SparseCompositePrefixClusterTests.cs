using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SparseCompositePrefixClusterTests(ClusterFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SparseCompositePrefixesKeepAllPublicAdapterRowsFreshUseReceiptsAndTwoCold(bool includeNull, bool includeMissing)
    {
        using var deadline = McpCallerDeadline.Create();
        await SparseCompositePrefixClusterFlow.RunAsync(fixture, includeNull, includeMissing, deadline.Token);
    }
}
