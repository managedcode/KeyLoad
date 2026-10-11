using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveTailUnrelatedCommitRf3Control
{
    private const string PrincipalPrefix = "live-cut-unrelated-";
    private const string GuidFormat = "N";

    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, CommitReceipt deletion, CancellationToken token)
    {
        var observeCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var head = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var unrelated = new PrincipalRecord(PrincipalPrefix + Guid.NewGuid().ToString(GuidFormat),
            scenario.Partition.TenantId, [], []);
        var result = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), unrelated, token));
        await SqlRf3Protocol.EqualAsync(unrelated, result);
        var retained = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(head, retained);
        await Assert.That(await observeCut()).IsGreaterThan(before);
    }
}
