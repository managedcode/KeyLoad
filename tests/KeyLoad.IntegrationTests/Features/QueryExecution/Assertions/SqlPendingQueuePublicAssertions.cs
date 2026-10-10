using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlPendingQueuePublicAssertions
{
    internal static async Task UnchangedAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Parked,
            new(QueueLifecyclePublicProtocol.Parked, MessageState.Cancelled, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Four, QueueLifecyclePublicProtocol.One, null, null, LeaseVersion: QueueLifecyclePublicProtocol.Two,
                DeliveryGeneration: QueueLifecyclePublicProtocol.Two, SafeFailureCode: "AttemptsExhausted"), false, token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state, QueueLifecyclePublicProtocol.Pending,
            new(QueueLifecyclePublicProtocol.Pending, MessageState.PendingDeadLetter, QueueLifecyclePublicProtocol.One,
                QueueLifecyclePublicProtocol.Three, QueueLifecyclePublicProtocol.Two, null, null, LeaseVersion: QueueLifecyclePublicProtocol.One,
                SafeFailureCode: "AttemptsExhausted"), true, token);
    }

    internal static async Task RowsAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, QueryPage page, CancellationToken token)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(QueueLifecyclePublicProtocol.Two);
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(
            new[] { QueueLifecyclePublicProtocol.Parked, QueueLifecyclePublicProtocol.Pending })).IsTrue();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThan(QueueLifecyclePublicProtocol.NoSequence);
        foreach (var row in page.Rows)
        {
            var native = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(new(state.Lane, row.EntityId), token));
            using var payload = native!.PayloadJson is null ? null : JsonDocument.Parse(native.PayloadJson);
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
        }
        await UnchangedAsync(callers, state, token);
    }
}
