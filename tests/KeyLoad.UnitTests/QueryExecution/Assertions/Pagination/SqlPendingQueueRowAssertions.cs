using System.Text.Json;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlPendingQueueRowAssertions
{
    internal static async Task RowAsync(DatabaseEngine database, QueueLifecycleTestState state, QueryRow row)
    {
        var execution = database.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, row.EntityId)!;
        using var payload = execution.PayloadJson is null ? null : JsonDocument.Parse(execution.PayloadJson);
        using var headers = execution.HeadersJson is null ? null : JsonDocument.Parse(execution.HeadersJson);
        var expected = JsonSerializer.Serialize(new
        {
            id = execution.Metadata.Id,
            state = execution.Metadata.State.ToString(),
            attempts = execution.Metadata.Attempts,
            stateVersion = execution.Metadata.StateVersion,
            notBefore = execution.Metadata.NotBefore,
            expiresAt = execution.Metadata.ExpiresAt,
            payload = payload?.RootElement ?? (JsonElement?)null,
            headers = headers?.RootElement ?? (JsonElement?)null
        }, JsonDefaults.Options);
        await Assert.That(row.Revision).IsEqualTo(execution.Metadata.StateVersion);
        await Assert.That(row.Json).IsEqualTo(expected);
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.RedactedFields.GetValueOrDefault().IsEmpty).IsTrue();
        await Assert.That(row.Sources).IsNull();
        if (row.EntityId == QueueLifecycleTestProtocol.Pending)
        {
            await QueueLifecycleImage.BodyAsync(database, state, row.EntityId, MessageState.PendingDeadLetter,
                QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.None);
        }
    }
}
