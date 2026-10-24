using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class LiveTailClusterAssertions
{
    internal static QueryRow UpdatedRow => new(FeedLiveClusterProtocol.First, FeedLiveClusterProtocol.UpdatedRevision,
        FeedLiveClusterProtocol.UpdatedProjected, true, [FeedLiveClusterProtocol.SecretField]);

    internal static async Task SnapshotTailAsync(LiveQuerySnapshot snapshot, LiveQueryPage tail,
        FeedLiveClusterScenario scenario, CommitReceipt receipt)
    {
        var rows = snapshot.Rows.ToDictionary(row => row.EntityId, StringComparer.Ordinal);
        foreach (var change in tail.Changes)
        {
            rows[change.Reference.Id] = change.Row!;
        }
        await SqlClusterProtocol.EqualAsync(new[] { UpdatedRow }, rows.Values.ToArray());
        await Assert.That(snapshot.Rows.Length).IsEqualTo(FeedLiveClusterProtocol.PageLimit);
        if (snapshot.ThroughSequence < FeedLiveClusterProtocol.UpdatedSequence)
        {
            await SqlClusterProtocol.EqualAsync(new[] { new QueryRow(FeedLiveClusterProtocol.First,
                FeedLiveClusterProtocol.FirstRevision, FeedLiveClusterProtocol.FirstProjected, true,
                [FeedLiveClusterProtocol.SecretField]) }, snapshot.Rows.ToArray());
            await SqlClusterProtocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveClusterProtocol.UpdatedSequence,
                receipt.Token, LiveQueryChangeKind.Upsert, scenario.StoredReference, FeedLiveClusterProtocol.UpdatedRevision, UpdatedRow) }, tail.Changes.ToArray());
        }
        else
        {
            await Assert.That(snapshot.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.UpdatedSequence);
            await SqlClusterProtocol.EqualAsync(new[] { UpdatedRow }, snapshot.Rows.ToArray());
            await Assert.That(tail.Changes.IsEmpty).IsTrue();
        }
        await Assert.That(tail.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.UpdatedSequence);
        await Assert.That(tail.HasMore).IsFalse();

    }

    internal static async Task AllPathsAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadLiveQueryRequest request,
        LiveQueryPage expected, CommitReceipt receipt, CancellationToken token)
    {
        ImmutableArray<QueryRow> rows = expected.ThroughSequence == FeedLiveClusterProtocol.DeletedSequence
            ? [] : expected.ThroughSequence == FeedLiveClusterProtocol.UpdatedSequence
                ? [UpdatedRow] : [LiveRepairedClusterAssertions.Row];
        Task<QueryPage> SdkQuery() => QueryAsync(reader, scenario, token);
        Task<QueryPage> McpQuery() => OfficialQueryAsync(reader, scenario, token);
        var querySql = SqlClusterProtocol.Call(scenario.Partition, LiveQueryCutClusterAssertions.QueryTool, scenario.Query);
        var sdkCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, SdkQuery, token);
        var mcpCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, McpQuery, token);
        var sdkSqlCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, () => SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, querySql, token), token);
        var mcpSqlCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            rows, () => SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, querySql, token), token);
        await LivePageClusterAssertions.RequireAsync(expected, request, sdkCut,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)));
        await LivePageClusterAssertions.RequireAsync(expected, request, mcpCut, async next =>
        {
            var reply = await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, next, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveClusterProtocol.Secret);
            return (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(reply)).Value;
        });
        await LivePageClusterAssertions.RequireAsync(expected, request, sdkSqlCut,
            next => SqlClusterProtocol.SdkAsync<LiveQueryPage>(reader.Sdk,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.LiveRead, next), token));
        await LivePageClusterAssertions.RequireAsync(expected, request, mcpSqlCut,
            next => SqlClusterProtocol.McpAsync<LiveQueryPage>(reader.Mcp,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.LiveRead, next), token));
    }

    internal static async Task<QueryPage> QueryAsync(RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));

    private static async Task<QueryPage> OfficialQueryAsync(RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CancellationToken token)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
            LiveQueryCutClusterAssertions.QueryTool, scenario.Query, token))).Value;

    internal static async Task DeniedAsync(RequestCqrsClusterCallers reader, ReadLiveQueryRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadLiveQueryAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, request, token),
            expected, dispatched: true);
        await LivePageClusterAssertions.DeniedSqlAsync(reader,
            SqlClusterProtocol.Call(request.Query.Partition, FeedLiveClusterProtocol.LiveRead, request), expected, token);
    }

}
