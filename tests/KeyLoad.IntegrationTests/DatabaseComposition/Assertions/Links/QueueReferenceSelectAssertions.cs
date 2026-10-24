using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.RelationalStorage;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class QueueReferenceSelectAssertions
{
    private const string MessageIdFieldKey = "message_id";
    private const string ScoreFieldKey = "score";
    private const string CustomerNameFieldKey = "customer_name";
    internal static async Task PageAsync(RequestCqrsClusterCallers callers, QueueReferenceSelectState state,
        bool masked, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(QueueReferenceSelectProtocol.Request(state.Partition), token));
        var sql = await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk, QueueReferenceSelectProtocol.Operation(state.Partition), token);
        var official = await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp, QueueReferenceSelectProtocol.Operation(state.Partition), token);
        var query = (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(
            McpCallerTools.QueryExecute, QueueReferenceSelectProtocol.Request(state.Partition), token))).Value;
        await Assert.That(direct.Cursor).IsNull();
        await Assert.That(direct.AccessPath).IsEqualTo("bounded-queue-entity-reference-inner-join");
        await Assert.That(direct.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        var expected = new List<(string Message, string Target, string Name)> { ("a", "c1", "Ada") };
        if (state.Repair is not null)
        {
            expected.Add(("b", "m", "Max"));
        }
        await Assert.That(direct.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = direct.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Message);
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Sources!.Value).IsEquivalentTo(new QueryRowSource[]
                { new("q", item.Message, 1), new("d", item.Target, 1) }, CollectionOrdering.Matching);
            await Assert.That(row.Redacted).IsEqualTo(masked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(masked ? new[] { "d.name" } : [], CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(MessageIdFieldKey).GetString()).IsEqualTo(item.Message);
            await Assert.That(json.RootElement.GetProperty(ScoreFieldKey).GetInt32()).IsEqualTo(7);
            await Assert.That(json.RootElement.GetProperty(CustomerNameFieldKey).GetString()).IsEqualTo(masked ? null : item.Name);
        }
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, sql);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, official);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, query);
    }
}
