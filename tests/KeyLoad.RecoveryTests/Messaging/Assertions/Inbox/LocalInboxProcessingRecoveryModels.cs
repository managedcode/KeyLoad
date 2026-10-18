using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryModels
{
    internal static async Task RequireAsync(DatabaseEngine database, DateTimeOffset time, bool committed)
    {
        if (committed)
        {
            await LocalInboxProcessingRecoveryQueueCut.RequireAsync(database.Store);
        }
        var partition = LocalInboxProcessingCrashContract.Partition;
        var reference = new EntityRef(partition, LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect);
        var document = database.GetDocument(CrashFixtureValues.Principal, reference);
        var output = database.InspectMessage(CrashFixtureValues.Principal,
            LocalInboxProcessingCrashContract.OutputLane, LocalInboxProcessingCrashContract.Message);
        var streamRef = new StreamRef(partition, LocalInboxProcessingCrashContract.Events, LocalInboxProcessingCrashContract.Effect);
        var events = database.ReadStream(CrashFixtureValues.Principal, streamRef);
        await LocalInboxProcessingRecoveryImage.ExactAsync(events.Head, new StreamHead(committed ? 1L : 0L, 1L, 1L));
        await Assert.That(events.HasMore).IsFalse();
        if (!committed)
        {
            await Assert.That(document).IsNull();
            await Assert.That(output).IsNull();
            await Assert.That(events.Events).IsEmpty();
            return;
        }
        await LocalInboxProcessingRecoveryImage.ExactAsync(document,
            new DocumentResult(reference, 1L, LocalInboxProcessingCrashContract.Payload, false, []));
        await LocalInboxProcessingRecoveryImage.ExactAsync(output,
            new MessageInspection(new(LocalInboxProcessingCrashContract.Message, MessageState.Ready, 0, 1L, 1L, null, null),
                LocalInboxProcessingCrashContract.Payload, "{}"));
        var item = await Assert.That(events.Events).HasSingleItem();
        await LocalInboxProcessingRecoveryImage.ExactAsync(item, new EventRecord(streamRef, 1L, 1L,
            new(LocalInboxProcessingCrashContract.Effect, "Processed", LocalInboxProcessingCrashContract.Payload), time));
    }
}
