using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlModelViewClusterParityAssertions
{
    internal static async Task AssertEventRowsAsync(ImmutableArray<EventRecord> execution, QueryPage page)
    {
        await Assert.That(execution.Length).IsEqualTo(SqlModelViewClusterScenario.ExpectedEvents.Length);
        await Assert.That(page.Rows.Length).IsEqualTo(execution.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:events");
        for (var index = 0; index < execution.Length; index++)
        {
            var record = execution[index];
            var row = page.Rows[index];
            var expected = SqlModelViewClusterScenario.ExpectedEvents[index];
            await Assert.That(record.Data).IsEqualTo(expected);
            await Assert.That(row.EntityId).IsEqualTo(expected.EventId);
            await Assert.That(row.Revision).IsEqualTo(index + 1L);
            await AssertEventJsonAsync(row, record);
        }
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(
            new[] { SqlModelViewClusterScenario.FirstEventId, SqlModelViewClusterScenario.SecondEventId })).IsTrue();
    }

    private static async Task AssertEventJsonAsync(QueryRow row, EventRecord record)
    {
        using var json = JsonDocument.Parse(row.Json);
        var value = json.RootElement;
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.EventId).GetString()).IsEqualTo(record.Data.EventId);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.EventType).GetString()).IsEqualTo(record.Data.EventType);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.SchemaVersion).GetInt32()).IsEqualTo(record.Data.SchemaVersion);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Revision).GetInt64()).IsEqualTo(record.Revision);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.EventSequence).GetInt64()).IsEqualTo(record.EventSequence);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.RecordedAt).GetDateTimeOffset()).IsEqualTo(record.RecordedAt);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Payload).GetRawText()).IsEqualTo(record.Data.PayloadJson);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Headers).GetRawText()).IsEqualTo(record.Data.HeadersJson);
        await SqlModelViewClusterAssertions.AssertNoDeliveryAuthorityAsync(value);
    }

    internal static async Task AssertQueueRowsAsync(
        (MessageInspection First, MessageInspection Second) execution, QueryPage page)
    {
        var expected = new[] { execution.First, execution.Second };
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("model-scan:queue-messages");
        for (var index = 0; index < expected.Length; index++)
        {
            await AssertQueueRowAsync(page.Rows[index], expected[index]);
        }
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(
            new[] { SqlModelViewClusterScenario.FirstMessageId, SqlModelViewClusterScenario.SecondMessageId })).IsTrue();
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
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Id).GetString()).IsEqualTo(inspection.Metadata.Id);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.State).GetString()).IsEqualTo(nameof(MessageState.Ready));
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Attempts).GetInt32()).IsEqualTo(inspection.Metadata.Attempts);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.StateVersion).GetInt64()).IsEqualTo(inspection.Metadata.StateVersion);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.NotBefore).GetDateTimeOffset()).IsEqualTo(
            inspection.Metadata.NotBefore!.Value);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.ExpiresAt).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(inspection.Metadata.ExpiresAt).IsNull();
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Payload).GetRawText()).IsEqualTo(inspection.PayloadJson);
        await Assert.That(value.GetProperty(SqlModelViewClusterJsonKeys.Headers).GetRawText()).IsEqualTo(inspection.HeadersJson);
        await SqlModelViewClusterAssertions.AssertNoDeliveryAuthorityAsync(value);
    }

    internal static async Task AssertEquivalentRowsAsync(QueryPage sdkSql, QueryPage sdkAst,
        QueryPage mcpSql, QueryPage mcpAst, string expectedAccessPath)
    {
        foreach (var page in new[] { sdkSql, sdkAst, mcpSql, mcpAst })
        {
            await Assert.That(page.AccessPath).IsEqualTo(expectedAccessPath);
            await Assert.That(page.Cursor).IsNull();
        }
        await SqlClusterProtocol.EqualAsync(sdkSql.Rows, sdkAst.Rows);
        await SqlClusterProtocol.EqualAsync(sdkSql.Rows, mcpSql.Rows);
        await SqlClusterProtocol.EqualAsync(sdkSql.Rows, mcpAst.Rows);
    }

    internal static async Task<(MessageInspection First, MessageInspection Second)> InspectBothAsync(
        KeyLoadClient sdk, SqlModelViewClusterScenario scenario, CancellationToken cancellationToken)
    {
        var first = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            scenario.Inspect(SqlModelViewClusterScenario.FirstMessageId), cancellationToken));
        var second = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            scenario.Inspect(SqlModelViewClusterScenario.SecondMessageId), cancellationToken));
        return (first!, second!);
    }

    internal static async Task AssertQueueUnchangedAsync(MessageInspection before, MessageInspection after)
    {
        await SqlClusterProtocol.EqualAsync(before, after);
        await Assert.That(after.Metadata.State).IsEqualTo(before.Metadata.State);
        await Assert.That(after.Metadata.Attempts).IsEqualTo(before.Metadata.Attempts);
        await Assert.That(after.Metadata.StateVersion).IsEqualTo(before.Metadata.StateVersion);
        await Assert.That(after.Metadata.ReadySequence).IsEqualTo(before.Metadata.ReadySequence);
        await Assert.That(after.Metadata.LeaseOwner).IsEqualTo(before.Metadata.LeaseOwner);
        await Assert.That(after.Metadata.LeaseVersion).IsEqualTo(before.Metadata.LeaseVersion);
        await Assert.That(after.Metadata.DeliveryGeneration).IsEqualTo(before.Metadata.DeliveryGeneration);
    }
}
