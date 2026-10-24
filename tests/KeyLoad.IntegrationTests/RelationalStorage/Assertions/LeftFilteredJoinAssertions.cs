using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal static class LeftFilteredJoinAssertions
{
    private const string OrderIdFieldKey = "order_id";
    private const string TotalFieldKey = "total";
    private const string CustomerNameFieldKey = "customer_name";
    internal static async Task PageAsync(RequestCqrsClusterCallers callers, LeftFilteredJoinState state,
        bool masked, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.QueryAsync(LeftFilteredJoinProtocol.Query(state.Partition), token));
        var unified = await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk,
            LeftFilteredJoinProtocol.Request(state.Partition), token);
        var official = await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp,
            LeftFilteredJoinProtocol.Request(state.Partition), token);
        var queryReply = (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(
            McpCallerTools.QueryExecute, LeftFilteredJoinProtocol.Query(state.Partition), token))).Value;
        await Assert.That(direct.Cursor).IsNull();
        await Assert.That(direct.AccessPath).IsEqualTo("bounded-primary-key-inner-join");
        await Assert.That(direct.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await Assert.That(direct.Rows).HasSingleItem();
        var row = direct.Rows.Single();
        await Assert.That(row.EntityId).IsEqualTo("a");
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Sources!.Value).IsEquivalentTo(
            new QueryRowSource[] { new("l", "a", 1), new("r", "c1", 1) }, CollectionOrdering.Matching);
        await Assert.That(row.Redacted).IsEqualTo(masked);
        await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(
            masked ? new[] { "r.name" } : [], CollectionOrdering.Matching);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
        await Assert.That(json.RootElement.GetProperty(OrderIdFieldKey).GetString()).IsEqualTo("a");
        await Assert.That(json.RootElement.GetProperty(TotalFieldKey).GetInt32()).IsEqualTo(7);
        await Assert.That(json.RootElement.GetProperty(CustomerNameFieldKey).GetString()).IsEqualTo(masked ? null : "Ada");
        await SamePageAsync(direct, unified);
        await SamePageAsync(direct, official);
        await SamePageAsync(direct, queryReply);
        var literal = LeftFilteredJoinProtocol.Query(state.Partition) with
        {
            Sql = LeftFilteredJoinProtocol.Prefix + "WHERE l.total = 7 " + LeftFilteredJoinProtocol.Order,
            Parameters = null
        };
        await SamePageAsync(direct,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(literal, token)));
    }

    // The direct row's sources are asserted literally above; the projection has no customer_id output.
    private static async Task SamePageAsync(QueryPage direct, QueryPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(direct.CutPosition);
        await SqlClusterProtocol.EqualAsync(direct with
        {
            CutPosition = actual.CutPosition
        }, actual);
    }
}
