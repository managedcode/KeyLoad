using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlPendingQueueRowAssertions
{
    internal static async Task RowAsync(DatabaseEngine database, QueueLifecycleTestState state, QueryRow row)
    {
        var native = database.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, row.EntityId)!;
        using var payload = native.PayloadJson is null ? null : JsonDocument.Parse(native.PayloadJson);
        using var headers = native.HeadersJson is null ? null : JsonDocument.Parse(native.HeadersJson);
        var expected = JsonSerializer.Serialize(new
        {
            id = native.Metadata.Id,
            state = native.Metadata.State.ToString(),
            attempts = native.Metadata.Attempts,
            stateVersion = native.Metadata.StateVersion,
            notBefore = native.Metadata.NotBefore,
            expiresAt = native.Metadata.ExpiresAt,
            payload = payload?.RootElement ?? (JsonElement?)null,
            headers = headers?.RootElement ?? (JsonElement?)null
        }, JsonDefaults.Options);
        await Assert.That(row.Revision).IsEqualTo(native.Metadata.StateVersion);
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
