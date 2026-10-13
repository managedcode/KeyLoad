using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyRf3Current
{
    private const string FullScan = "bounded-full-scan";

    internal static async Task ObserveAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, bool updated,
        CancellationToken token)
    {
        var revision = updated ? FeedLiveRf3Protocol.UpdatedRevision : FeedLiveRf3Protocol.FirstRevision;
        var json = updated ? FeedLiveRf3Protocol.UpdatedProjected : FeedLiveRf3Protocol.FirstProjected;
        var expected = new QueryRow(FeedLiveRf3Protocol.First, revision, json, true,
            [FeedLiveRf3Protocol.SecretField]);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await PageAsync(sdk, expected);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.QueryAst, scenario.Query, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, expected);
        var document = new DocumentResult(scenario.Reference, revision, json, true, [FeedLiveRf3Protocol.SecretField]);
        var found = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(scenario.Reference,
            scenario.Receipt.Token, token));
        await SqlRf3Protocol.EqualAsync(document, found.Document);
        var official = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference, scenario.Receipt.Token), token);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        await SqlRf3Protocol.EqualAsync(document,
            (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(official)).Value.Document);
        var raw = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlRf3Protocol.EqualAsync(FeedLiveRf3NoEffects.Rows(revision,
            updated ? FeedLiveRf3Protocol.UpdatedJson : FeedLiveRf3Protocol.FirstJson), raw.Rows.ToArray());
        await Assert.That(raw.Cursor).IsNull();
        await Assert.That(raw.AccessPath).IsEqualTo(FullScan);
    }

    internal static async Task BracketAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, bool updated,
        Func<Task> operation, CancellationToken token)
    {
        await ObserveAsync(administrator, reader, scenario, updated, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(before.Head.Tail).IsEqualTo(updated
            ? FeedLiveRf3Protocol.UpdatedSequence : FeedLiveRf3Protocol.HiddenSequence);
        await SqlRf3Protocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
        await operation();
        await ObserveAsync(administrator, reader, scenario, updated, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(before, after);
        await SqlRf3Protocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }

    private static async Task PageAsync(QueryPage page, QueryRow row)
    {
        await SqlRf3Protocol.EqualAsync(new[] { row }, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(FullScan);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(0L);
    }
}
