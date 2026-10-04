using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewRf3ParityAssertions
{
    internal static async Task AssertEventRowsAsync(ImmutableArray<EventRecord> native, QueryPage page)
    {
        await Assert.That(native.Length).IsEqualTo(SqlModelViewRf3Scenario.ExpectedEvents.Length);
        await Assert.That(page.Rows.Length).IsEqualTo(native.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:events");
        for (var index = 0; index < native.Length; index++)
        {
            var record = native[index];
            var row = page.Rows[index];
            var expected = SqlModelViewRf3Scenario.ExpectedEvents[index];
            await Assert.That(record.Data).IsEqualTo(expected);
            await Assert.That(row.EntityId).IsEqualTo(expected.EventId);
            await Assert.That(row.Revision).IsEqualTo(index + 1L);
            await AssertEventJsonAsync(row, record);
        }
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(
            new[] { SqlModelViewRf3Scenario.FirstEventId, SqlModelViewRf3Scenario.SecondEventId })).IsTrue();
    }

    private static async Task AssertEventJsonAsync(QueryRow row, EventRecord record)
    {
        using var json = JsonDocument.Parse(row.Json);
        var value = json.RootElement;
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.EventId).GetString()).IsEqualTo(record.Data.EventId);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.EventType).GetString()).IsEqualTo(record.Data.EventType);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.SchemaVersion).GetInt32()).IsEqualTo(record.Data.SchemaVersion);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Revision).GetInt64()).IsEqualTo(record.Revision);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.EventSequence).GetInt64()).IsEqualTo(record.EventSequence);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.RecordedAt).GetDateTimeOffset()).IsEqualTo(record.RecordedAt);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Payload).GetRawText()).IsEqualTo(record.Data.PayloadJson);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Headers).GetRawText()).IsEqualTo(record.Data.HeadersJson);
        await SqlModelViewRf3Assertions.AssertNoDeliveryAuthorityAsync(value);
    }

    internal static async Task AssertQueueRowsAsync(
        (MessageInspection First, MessageInspection Second) native, QueryPage page)
    {
        var expected = new[] { native.First, native.Second };
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:queue-messages");
        for (var index = 0; index < expected.Length; index++)
        {
            await AssertQueueRowAsync(page.Rows[index], expected[index]);
        }
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(
            new[] { SqlModelViewRf3Scenario.FirstMessageId, SqlModelViewRf3Scenario.SecondMessageId })).IsTrue();
    }

    private static async Task AssertQueueRowAsync(QueryRow row, MessageInspection inspection)
    {
        await Assert.That(row.EntityId).IsEqualTo(inspection.Metadata.Id);
        await Assert.That(row.Revision).IsEqualTo(inspection.Metadata.StateVersion);
        await Assert.That(inspection.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(inspection.Metadata.Attempts).IsEqualTo(0);
        await Assert.That(inspection.Metadata.LeaseOwner).IsNull();
        await Assert.That(inspection.PayloadJson).IsNotNull();
        await Assert.That(inspection.HeadersJson).IsNotNull();
        using var json = JsonDocument.Parse(row.Json);
        var value = json.RootElement;
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Id).GetString()).IsEqualTo(inspection.Metadata.Id);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.State).GetString()).IsEqualTo(nameof(MessageState.Ready));
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Attempts).GetInt32()).IsEqualTo(inspection.Metadata.Attempts);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.StateVersion).GetInt64()).IsEqualTo(inspection.Metadata.StateVersion);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.NotBefore).GetDateTimeOffset()).IsEqualTo(
            inspection.Metadata.NotBefore!.Value);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.ExpiresAt).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(inspection.Metadata.ExpiresAt).IsNull();
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Payload).GetRawText()).IsEqualTo(inspection.PayloadJson);
        await Assert.That(value.GetProperty(SqlModelViewRf3JsonKeys.Headers).GetRawText()).IsEqualTo(inspection.HeadersJson);
        await SqlModelViewRf3Assertions.AssertNoDeliveryAuthorityAsync(value);
    }

    internal static async Task AssertEquivalentRowsAsync(QueryPage sdkSql, QueryPage sdkAst,
        QueryPage mcpSql, QueryPage mcpAst, string expectedAccessPath)
    {
        foreach (var page in new[] { sdkSql, sdkAst, mcpSql, mcpAst })
        {
            await Assert.That(page.AccessPath).IsEqualTo(expectedAccessPath);
            await Assert.That(page.Cursor).IsNull();
        }
        await SqlRf3Protocol.EqualAsync(sdkSql.Rows, sdkAst.Rows);
        await SqlRf3Protocol.EqualAsync(sdkSql.Rows, mcpSql.Rows);
        await SqlRf3Protocol.EqualAsync(sdkSql.Rows, mcpAst.Rows);
    }

    internal static async Task<(MessageInspection First, MessageInspection Second)> InspectBothAsync(
        KeyLoadClient sdk, SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            scenario.Inspect(SqlModelViewRf3Scenario.FirstMessageId), cancellationToken));
        var second = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            scenario.Inspect(SqlModelViewRf3Scenario.SecondMessageId), cancellationToken));
        return (first!, second!);
    }

    internal static async Task AssertQueueUnchangedAsync(MessageInspection before, MessageInspection after)
    {
        await SqlRf3Protocol.EqualAsync(before, after);
        await Assert.That(after.Metadata.State).IsEqualTo(before.Metadata.State);
        await Assert.That(after.Metadata.Attempts).IsEqualTo(before.Metadata.Attempts);
        await Assert.That(after.Metadata.StateVersion).IsEqualTo(before.Metadata.StateVersion);
        await Assert.That(after.Metadata.ReadySequence).IsEqualTo(before.Metadata.ReadySequence);
        await Assert.That(after.Metadata.LeaseOwner).IsEqualTo(before.Metadata.LeaseOwner);
        await Assert.That(after.Metadata.LeaseVersion).IsEqualTo(before.Metadata.LeaseVersion);
        await Assert.That(after.Metadata.DeliveryGeneration).IsEqualTo(before.Metadata.DeliveryGeneration);
    }
}
