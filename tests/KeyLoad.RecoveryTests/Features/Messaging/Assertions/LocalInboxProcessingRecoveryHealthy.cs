using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryHealthy
{
    internal static async Task RequireAsync(DatabaseEngine database, KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt receipt, long position)
    {
        await LocalInboxProcessingRecoveryImage.ExactAsync(receipt, new CommitReceipt(operation.Id,
            new(store.Identity.Incarnation, LocalInboxProcessingCrashContract.Partition.AtomicPartitionId, position, 1L),
            [new("putDocument", LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy, 1L),
             new("Ack", LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.FinalVersion)],
            store.Identity.Durability));
        await ModelsAsync(database);
    }

    internal static async Task ModelsAsync(DatabaseEngine database)
    {
        var reference = new EntityRef(LocalInboxProcessingCrashContract.Partition,
            LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Healthy);
        await LocalInboxProcessingRecoveryImage.ExactAsync(database.GetDocument(CrashFixtureValues.Principal, reference),
            new DocumentResult(reference, 1L, LocalInboxProcessingCrashContract.Payload, false, []));
        await LocalInboxProcessingRecoveryImage.ExactAsync(database.InspectMessage(CrashFixtureValues.Principal,
            LocalInboxProcessingCrashContract.OutputLane, LocalInboxProcessingCrashContract.Message),
            new MessageInspection(new(LocalInboxProcessingCrashContract.Message, MessageState.Acked, 1,
                LocalInboxProcessingCrashContract.FinalVersion, 1L, null, null, LeaseVersion: 1L), null, null));
    }
}
