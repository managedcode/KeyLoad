using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueuePendingAuthorityEffects
{
    internal static async Task RequireAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state)
    {
        foreach (var processed in state.Processed)
        {
            var reference = new EntityRef(state.Flow.Partition, QueueLifecycleTestProtocol.Collection, processed.Id)
            {
                Lifetime = state.DocumentLifetime
            };
            var document = owner.GetDocument(QueueLifecycleTestProtocol.Administrator, reference)!;
            await Assert.That(document.Reference).IsEqualTo(reference);
            await Assert.That(document.Revision).IsEqualTo(1);
            await Assert.That(document.Json).IsEqualTo(QueueLifecycleTestProtocol.Payload);
            await Assert.That(document.Redacted).IsFalse();
            await Assert.That(document.RedactedFields).IsEmpty();
            var inbox = store.Read(view => view.GetRecord<InboxRecord>(QueuePendingAuthorityOperations.Key(state.Flow,
                QueuePendingAuthorityProtocol.Inbox, processed.Id, QueuePendingAuthorityProtocol.Handler, 1)))!;
            var processing = processed.Request;
            var fingerprint = JsonData.Fingerprint(new
            {
                processing.Lane,
                MessageId = processed.Id,
                processing.HandlerScope,
                processing.ExecutionGeneration,
                processing.Effects,
                Id = QueueLifecycleTestProtocol.Administrator
            });
            await Assert.That(inbox.Fingerprint).IsEqualTo(fingerprint);
            await Assert.That(JsonSerializer.Serialize(inbox.Receipt, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(processed.Receipt, JsonDefaults.Options));
        }
        var page = store.Read(view => view.Scan(QueuePendingAuthorityOperations.Key(state.Flow,
            QueuePendingAuthorityProtocol.Inbox), QueueLifecycleTestProtocol.ImageRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(state.Processed.Count);
    }
}
