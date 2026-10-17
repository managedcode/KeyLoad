using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateEventsQueries
{
    internal static async Task VerifyAsync(FunctionalGateContext context, StreamRef stream, int iteration,
        CommitReceipt receipt, CancellationToken token)
    {
        var request = FunctionalGateEventsProtocol.Query(stream, iteration);
        var sdk = await context.SdkAsync(context.Reader.Sdk.QueryAsync(request, token));
        var mcp = await context.McpAsync<QueryPage>(context.Reader, McpCallerTools.QueryExecute, request, token);
        await PageAsync(context, sdk, iteration, receipt.Token.Position);
        await PageAsync(context, mcp, iteration, receipt.Token.Position);
        await FunctionalGateEventsAssertions.SameAsync(sdk.Rows, mcp.Rows);
    }

    private static async Task PageAsync(FunctionalGateContext context, QueryPage page, int iteration, long minimumCut)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(page.AccessPath).IsEqualTo("bounded-event-entity-reference-inner-join");
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        var row = page.Rows.Single();
        var expected = FunctionalGateEventsProtocol.Event(context, iteration, linked: true);
        await Assert.That(row.EntityId).IsEqualTo(expected.EventId);
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.RedactedFields!.Value.Length).IsEqualTo(0);
        await Assert.That(row.Sources!.Value).IsEquivalentTo(new QueryRowSource[]
            { new("e", expected.EventId, 1), new("d", context.Anchor.Id, 1) }, CollectionOrdering.Matching);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
        await Assert.That(json.RootElement.GetProperty(FunctionalGateEventsProtocol.EventIdColumn).GetString()).IsEqualTo(expected.EventId);
        await Assert.That(json.RootElement.GetProperty(FunctionalGateEventsProtocol.IterationField).GetInt32()).IsEqualTo(iteration);
        await Assert.That(json.RootElement.GetProperty(FunctionalGateEventsProtocol.TargetIdColumn).GetString()).IsEqualTo(context.Anchor.Id);
    }
}
