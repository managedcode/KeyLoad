using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlClusterAssertions
{
    internal static async Task FullAsync(TopicSqlClusterState state, QueryPage page)
    {
        await Assert.That(state.Original.Source).IsEqualTo(state.Source);
        await Assert.That(state.Original.Head).IsEqualTo(new EventSourceHead(TopicSqlClusterProtocol.SecondPosition,
            TopicSqlClusterProtocol.FirstPosition, TopicSqlClusterProtocol.FirstPosition));
        await Assert.That(state.Original.HasMore).IsFalse();
        await Assert.That(page.Rows.Length).IsEqualTo(TopicSqlClusterState.Data.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThan(0);
        await Assert.That(page.AccessPath).IsEqualTo(TopicSqlClusterProtocol.AccessPath);
        for (var index = 0; index < TopicSqlClusterState.Data.Length; index++)
        {
            var expectedData = TopicSqlClusterState.Data[index];
            var native = state.Original.Events[index];
            await SqlClusterProtocol.EqualAsync(expectedData, native.Data);
            await Assert.That(native.Source).IsEqualTo(state.Source);
            await Assert.That(native.Position).IsEqualTo(index + TopicSqlClusterProtocol.FirstPosition);
            var row = page.Rows[index];
            await Assert.That(row.EntityId).IsEqualTo(expectedData.EventId);
            await Assert.That(row.Revision).IsEqualTo(native.Position);
            using var payload = JsonDocument.Parse(expectedData.PayloadJson);
            using var headers = JsonDocument.Parse(expectedData.HeadersJson);
            var expected = JsonSerializer.Serialize(new
            {
                eventId = expectedData.EventId,
                eventType = expectedData.EventType,
                schemaVersion = expectedData.SchemaVersion,
                position = native.Position,
                eventSequence = native.EventSequence,
                recordedAt = native.RecordedAt,
                payload = payload.RootElement,
                headers = headers.RootElement
            }, JsonDefaults.Options);
            await Assert.That(row.Json).IsEqualTo(expected);
            await Assert.That(row.Redacted).IsFalse();
            await Assert.That(row.RedactedFields.GetValueOrDefault().IsDefaultOrEmpty).IsTrue();
            await SqlModelViewClusterAssertions.AssertNoDeliveryAuthorityAsync(JsonSerializer.Deserialize<JsonElement>(row.Json));
        }
    }

    internal static async Task RedactedAsync(QueryPage page)
        => await SqlModelViewClusterAssertions.AssertRedactedRowsAsync(page,
            TopicSqlClusterProtocol.Secret, TopicSqlClusterProtocol.HeaderSecret);
}
