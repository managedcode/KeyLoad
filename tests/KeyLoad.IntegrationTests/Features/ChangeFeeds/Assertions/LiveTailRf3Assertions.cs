using System.Collections.Immutable;
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
        {
            rows[change.Reference.Id] = change.Row!;
        }
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

    }

    internal static async Task AllPathsAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest request,
        LiveQueryPage expected, CommitReceipt receipt, CancellationToken token)
    {
        ImmutableArray<QueryRow> rows = expected.ThroughSequence == FeedLiveRf3Protocol.DeletedSequence
            ? [] : expected.ThroughSequence == FeedLiveRf3Protocol.UpdatedSequence
                ? [UpdatedRow] : [LiveRepairedRf3Assertions.Row];
        Task<QueryPage> SdkQuery() => QueryAsync(reader, scenario, token);
        Task<QueryPage> McpQuery() => OfficialQueryAsync(reader, scenario, token);
        var querySql = SqlRf3Protocol.Call(scenario.Partition, LiveQueryCutRf3Assertions.QueryTool, scenario.Query);
        var sdkCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, SdkQuery, token);
        var mcpCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, McpQuery, token);
        var sdkSqlCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, () => SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, querySql, token), token);
        var mcpSqlCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, () => SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, querySql, token), token);
        await LivePageRf3Assertions.RequireAsync(expected, request, sdkCut,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)));
        await LivePageRf3Assertions.RequireAsync(expected, request, mcpCut, async next =>
        {
            var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, next, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
            return (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(reply)).Value;
        });
        await LivePageRf3Assertions.RequireAsync(expected, request, sdkSqlCut,
            next => SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
        await LivePageRf3Assertions.RequireAsync(expected, request, mcpSqlCut,
            next => SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
    }

    internal static async Task<QueryPage> QueryAsync(RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));

    private static async Task<QueryPage> OfficialQueryAsync(RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CancellationToken token)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
            LiveQueryCutRf3Assertions.QueryTool, scenario.Query, token))).Value;

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
