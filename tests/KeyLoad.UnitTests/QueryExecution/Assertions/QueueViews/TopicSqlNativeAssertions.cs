using System.Text.Json;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class TopicSqlNativeAssertions
{
    internal static async Task RecordsAsync(EventSourcePage native, QueryPage page)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(native.Events.Length);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThan(0);
        await Assert.That(page.AccessPath).IsEqualTo(TopicSqlProtocol.AccessPath);
        foreach (var record in native.Events)
        {
            var row = page.Rows.Single(value => value.EntityId == record.Data.EventId);
            var expectedPayload = record.Position == TopicSqlProtocol.FirstPosition ? TopicSqlProtocol.Payload : TopicSqlProtocol.NextPayload;
            var expectedType = record.Position == TopicSqlProtocol.FirstPosition ? TopicSqlProtocol.Created : TopicSqlProtocol.Updated;
            await Assert.That(record.Data.PayloadJson).IsEqualTo(expectedPayload);
            await Assert.That(record.Data.HeadersJson).IsEqualTo(TopicSqlProtocol.Headers);
            await Assert.That(record.Data.EventType).IsEqualTo(expectedType);
            await Assert.That(record.Data.SchemaVersion).IsEqualTo(TopicSqlProtocol.FirstPosition);
            await Assert.That(record.Source.Kind).IsEqualTo(EventSourceKind.Topic);
            await Assert.That(record.Source.Resource).IsEqualTo(TopicSqlProtocol.Topic);
            await Assert.That(record.Source.StreamId).IsNull();
            using var payload = JsonDocument.Parse(expectedPayload);
            using var headers = JsonDocument.Parse(TopicSqlProtocol.Headers);
            var expected = JsonSerializer.Serialize(new
            {
                eventId = record.Data.EventId,
                eventType = expectedType,
                schemaVersion = TopicSqlProtocol.FirstPosition,
                position = record.Position,
                eventSequence = record.EventSequence,
                recordedAt = record.RecordedAt,
                payload = payload.RootElement,
                headers = headers.RootElement
            }, JsonDefaults.Options);
            await Assert.That(row.Json).IsEqualTo(expected);
            await Assert.That(row.Revision).IsEqualTo(record.Position);
            await Assert.That(row.Redacted).IsFalse();
            await Assert.That(row.RedactedFields.GetValueOrDefault().IsDefaultOrEmpty).IsTrue();
        }
    }

    internal static async Task UnchangedAsync(TestDatabase database, string[] bytes, long position)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    internal static async Task RedactedAsync(QueryPage page)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(TopicSqlProtocol.SecondPosition);
        foreach (var row in page.Rows)
        {
            await Assert.That(row.Redacted).IsTrue();
            await Assert.That(row.Json).DoesNotContain(TopicSqlProtocol.Secret);
            await Assert.That(row.Json).DoesNotContain(TopicSqlProtocol.HeaderSecret);
            using var value = JsonDocument.Parse(row.Json);
            await Assert.That(value.RootElement.GetProperty(TopicSqlProtocol.PayloadField)
                .TryGetProperty(TopicSqlProtocol.SecretField, out _)).IsFalse();
            await Assert.That(value.RootElement.GetProperty(TopicSqlProtocol.HeadersField)
                .TryGetProperty(TopicSqlProtocol.PrivateHeaderField, out _)).IsFalse();
        }
    }
}
