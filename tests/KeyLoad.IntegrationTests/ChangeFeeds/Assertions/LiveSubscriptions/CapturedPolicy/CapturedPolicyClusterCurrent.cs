using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class CapturedPolicyClusterCurrent
{
    private const string FullScan = "bounded-full-scan";

    internal static async Task ObserveAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, bool updated,
        CancellationToken token)
    {
        var revision = updated ? FeedLiveClusterProtocol.UpdatedRevision : FeedLiveClusterProtocol.FirstRevision;
        var json = updated ? FeedLiveClusterProtocol.UpdatedProjected : FeedLiveClusterProtocol.FirstProjected;
        var expected = new QueryRow(FeedLiveClusterProtocol.First, revision, json, true,
            [FeedLiveClusterProtocol.SecretField]);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await PageAsync(sdk, expected);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.QueryAst, scenario.Query, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveClusterProtocol.Secret);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, expected);
        var document = new DocumentResult(scenario.StoredReference, revision, json, true, [FeedLiveClusterProtocol.SecretField]);
        var found = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(scenario.Reference,
            scenario.Receipt.Token, token));
        await SqlClusterProtocol.EqualAsync(document, found.Document);
        var official = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference, scenario.Receipt.Token), token);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, scenario.Identity.Secret, FeedLiveClusterProtocol.Secret);
        await SqlClusterProtocol.EqualAsync(document,
            (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(official)).Value.Document);
        var raw = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlClusterProtocol.EqualAsync(FeedLiveClusterNoEffects.Rows(revision,
            updated ? FeedLiveClusterProtocol.UpdatedJson : FeedLiveClusterProtocol.FirstJson), raw.Rows.ToArray());
        await Assert.That(raw.Cursor).IsNull();
        await Assert.That(raw.AccessPath).IsEqualTo(FullScan);
    }

    internal static async Task BracketAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, bool updated,
        Func<Task> operation, CancellationToken token)
    {
        await ObserveAsync(administrator, reader, scenario, updated, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(before.Head.Tail).IsEqualTo(updated
            ? FeedLiveClusterProtocol.UpdatedSequence : FeedLiveClusterProtocol.HiddenSequence);
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
        await operation();
        await ObserveAsync(administrator, reader, scenario, updated, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(before, after);
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }

    private static async Task PageAsync(QueryPage page, QueryRow row)
    {
        await SqlClusterProtocol.EqualAsync(new[] { row }, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(FullScan);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(0L);
    }
}
