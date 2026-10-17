using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class EventReferenceSelectAssertions
{
    private const string EventIdField = "event_id";
    private const string ScoreField = "score";
    private const string CustomerNameField = "customer_name";

    internal static async Task SamePageAsync(QueryPage expected, QueryPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
        await SqlRf3Protocol.EqualAsync(expected with
        {
            CutPosition = actual.CutPosition
        }, actual);
    }

    internal static async Task PageAsync(RequestCqrsRf3Callers callers, EventReferenceSelectState state,
        bool masked, CancellationToken token)
    {
        var direct = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(EventReferenceSelectProtocol.Request(state.Partition), token));
        var sql = await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, EventReferenceSelectProtocol.Operation(state.Partition), token);
        var official = await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, EventReferenceSelectProtocol.Operation(state.Partition), token);
        var query = (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(
            McpCallerTools.QueryExecute, EventReferenceSelectProtocol.Request(state.Partition), token))).Value;
        await Assert.That(direct.Cursor).IsNull();
        await Assert.That(direct.AccessPath).IsEqualTo("bounded-event-entity-reference-inner-join");
        await Assert.That(direct.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        var expected = new List<(string Event, long Revision, string Target, string Name)> { ("z", 1, "c1", "Ada") };
        if (state.Repair is not null)
        {
            expected.Add(("a", 2, "m", "Max"));
        }
        await Assert.That(direct.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = direct.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Event);
            await Assert.That(row.Revision).IsEqualTo(item.Revision);
            await Assert.That(row.Sources!.Value).IsEquivalentTo(new QueryRowSource[]
                { new("e", item.Event, item.Revision), new("d", item.Target, 1) }, CollectionOrdering.Matching);
            await Assert.That(row.Redacted).IsEqualTo(masked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(masked ? new[] { "d.name" } : [], CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(EventIdField).GetString()).IsEqualTo(item.Event);
            await Assert.That(json.RootElement.GetProperty(ScoreField).GetInt32()).IsEqualTo(7);
            await Assert.That(json.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo(masked ? null : item.Name);
        }
        await SamePageAsync(direct, sql);
        await SamePageAsync(direct, official);
        await SamePageAsync(direct, query);
    }
}
