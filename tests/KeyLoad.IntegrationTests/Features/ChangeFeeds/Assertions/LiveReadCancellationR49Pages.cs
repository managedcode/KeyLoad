using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Pages
{
    internal static async Task<LiveQuerySnapshot> SnapshotAsync(LiveReadCancellationR49Owner owner,
        long revision, string json, long tail, CancellationToken token)
    {
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Caller.Sdk.StartLiveQueryAsync(new(owner.Query), token));
        await LiteralSnapshotAsync(snapshot, revision, json, tail);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        var reply = await owner.Caller.Mcp.CallAsync(FeedLiveRf3Protocol.LiveStart, new StartLiveQueryRequest(owner.Query), token);
        await PrivacyAsync(reply, owner);
        await LiteralSnapshotAsync((await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(reply)).Value, revision, json, tail);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        return snapshot;
    }

    internal static async Task HealthyAsync(LiveReadCancellationR49Owner owner, ReadLiveQueryRequest request,
        bool empty, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await owner.Caller.Sdk.ReadLiveQueryAsync(request, token));
        await LiteralPageAsync(sdk, owner, empty);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        var reply = await owner.Caller.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, request, token);
        await PrivacyAsync(reply, owner);
        await LiteralPageAsync((await McpCallerAssertions.SuccessAsync<LiveQueryPage>(reply)).Value, owner, empty);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        if (empty)
        { return; }
        var next = request with { Cursor = sdk.Cursor };
        var consumed = await McpCallerAssertions.SdkSuccessAsync(await owner.Caller.Sdk.ReadLiveQueryAsync(next, token));
        await LiteralPageAsync(consumed, owner, true);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        var consumedReply = await owner.Caller.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, next, token);
        await PrivacyAsync(consumedReply, owner);
        await LiteralPageAsync((await McpCallerAssertions.SuccessAsync<LiveQueryPage>(consumedReply)).Value, owner, true);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
    }

    private static async Task LiteralSnapshotAsync(LiveQuerySnapshot snapshot, long revision, string json, long tail)
    {
        await SqlRf3Protocol.EqualAsync(new[] { LiveReadCancellationR49Current.Row(revision, json) }, snapshot.Rows.ToArray());
        await Assert.That(snapshot.ThroughSequence).IsEqualTo(tail);
        await Assert.That(snapshot.CutPosition).IsGreaterThan(0L);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
    }

    private static async Task LiteralPageAsync(LiveQueryPage page, LiveReadCancellationR49Owner owner, bool empty)
    {
        LiveQueryChange[] expected = empty ? [] : [new(LiveReadCancellationR49Protocol.CurrentTail, owner.CurrentReceipt.Token,
            LiveQueryChangeKind.Upsert, owner.Reference, LiveReadCancellationR49Protocol.CurrentRevision,
            LiveReadCancellationR49Current.Row(LiveReadCancellationR49Protocol.CurrentRevision, LiveReadCancellationR49Protocol.CurrentJson))];
        await SqlRf3Protocol.EqualAsync(expected, page.Changes.ToArray());
        await Assert.That(page.ThroughSequence).IsEqualTo(LiveReadCancellationR49Protocol.CurrentTail);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    private static async Task PrivacyAsync(ModelContextProtocol.Protocol.CallToolResult reply, LiveReadCancellationR49Owner owner)
    {
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
            .Contains(owner.Identity.Secret, StringComparison.Ordinal)).IsFalse();
    }
}
