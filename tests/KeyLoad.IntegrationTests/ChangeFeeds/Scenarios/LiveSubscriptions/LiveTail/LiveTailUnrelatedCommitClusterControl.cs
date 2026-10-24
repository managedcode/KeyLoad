using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class LiveTailUnrelatedCommitClusterControl
{
    private const string PrincipalPrefix = "live-cut-unrelated-";
    private const string GuidFormat = "N";

    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, CommitReceipt deletion, CancellationToken token)
    {
        var observeCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var head = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var unrelated = new PrincipalRecord(PrincipalPrefix + Guid.NewGuid().ToString(GuidFormat),
            scenario.Partition.TenantId, [], []);
        var result = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), unrelated, token));
        await SqlClusterProtocol.EqualAsync(unrelated, result);
        var retained = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(head, retained);
        await Assert.That(await observeCut()).IsGreaterThan(before);
    }
}
