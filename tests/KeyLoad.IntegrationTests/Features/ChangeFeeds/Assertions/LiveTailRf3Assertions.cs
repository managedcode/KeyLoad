using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveTailRf3Assertions
{
    internal static QueryRow UpdatedRow => new(FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.UpdatedRevision,
        FeedLiveRf3Protocol.UpdatedProjected, true, [FeedLiveRf3Protocol.SecretField]);

    internal static async Task SnapshotTailAsync(LiveQuerySnapshot snapshot, LiveQueryPage tail,
        FeedLiveRf3Scenario scenario, CommitReceipt receipt)
    {
        var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        foreach (var change in tail.Changes)
        { rows[change.Reference.Id] = change.Row!; }
        await SqlRf3Protocol.EqualAsync(new[] { UpdatedRow }, rows.Values.ToArray());
        await Assert.That(snapshot.Rows.Length).IsEqualTo(FeedLiveRf3Protocol.PageLimit);
        if (snapshot.ThroughSequence < FeedLiveRf3Protocol.UpdatedSequence)
        {
            await SqlRf3Protocol.EqualAsync(new[] { new QueryRow(FeedLiveRf3Protocol.First,
                FeedLiveRf3Protocol.FirstRevision, FeedLiveRf3Protocol.FirstProjected, true,
                [FeedLiveRf3Protocol.SecretField]) }, snapshot.Rows.ToArray());
            await SqlRf3Protocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveRf3Protocol.UpdatedSequence,
                receipt.Token, LiveQueryChangeKind.Upsert, scenario.Reference, FeedLiveRf3Protocol.UpdatedRevision, UpdatedRow) }, tail.Changes.ToArray());
        }
        else
        {
            await Assert.That(snapshot.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.UpdatedSequence);
            await SqlRf3Protocol.EqualAsync(new[] { UpdatedRow }, snapshot.Rows.ToArray());
            await Assert.That(tail.Changes.IsEmpty).IsTrue();
        }
        await Assert.That(tail.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.UpdatedSequence);
        await Assert.That(tail.HasMore).IsFalse();
        await Assert.That(tail.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
    }

    internal static async Task AllPathsAsync(RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario,
        ReadLiveQueryRequest request, LiveQueryPage expected, CancellationToken token)
    {
        var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, request, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        var official = (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(reply)).Value;
        var sql = SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, request);
        var sdkSql = await SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk, sql, token);
        var mcpSql = await SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp, sql, token);
        await LivePageRf3Assertions.RequireAsync(expected, expected, request,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)));
        await LivePageRf3Assertions.RequireAsync(official, expected, request,
            async next => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
                await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, next, token))).Value);
        await LivePageRf3Assertions.RequireAsync(sdkSql, expected, request,
            next => SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
        await LivePageRf3Assertions.RequireAsync(mcpSql, expected, request,
            next => SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
    }
    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, ReadLiveQueryRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadLiveQueryAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, request, token),
            expected, dispatched: true);
        await LivePageRf3Assertions.DeniedSqlAsync(reader,
            SqlRf3Protocol.Call(request.Query.Partition, FeedLiveRf3Protocol.LiveRead, request), expected, token);
    }

}
