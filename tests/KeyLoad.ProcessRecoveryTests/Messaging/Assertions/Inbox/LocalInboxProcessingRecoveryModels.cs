using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class LocalInboxProcessingRecoveryModels
{
    internal static async Task<EntityRefLifetime> LifetimeAsync(Command<DatabaseCommand> original)
    {
        var partition = LocalInboxProcessingCrashContract.Partition;
        var definition = new ResourceDefinition(LocalInboxProcessingCrashContract.Documents, ResourceKind.Collection, partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await Assert.That(original.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(original.UserId).IsEqualTo(CrashFixtureValues.Principal);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await LocalInboxProcessingRecoveryImage.ExactAsync(
            ((ConfigureResourceRequest)original.Value.Input), request);
        var lifetime = ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
        return new(lifetime.Incarnation.Value);
    }

    internal static async Task RequireAsync(DatabaseEngine database, DateTimeOffset time, bool committed, EntityRefLifetime lifetime)
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
            new DocumentResult(reference with { Lifetime = lifetime }, 1L, LocalInboxProcessingCrashContract.Payload, false, []));
        await LocalInboxProcessingRecoveryImage.ExactAsync(output,
            new MessageInspection(new(LocalInboxProcessingCrashContract.Message, MessageState.Ready, 0, 1L, 1L, null, null),
                LocalInboxProcessingCrashContract.Payload, "{}"));
        var item = await Assert.That(events.Events).HasSingleItem();
        await LocalInboxProcessingRecoveryImage.ExactAsync(item, new EventRecord(streamRef, 1L, 1L,
            new(LocalInboxProcessingCrashContract.Effect, "Processed", LocalInboxProcessingCrashContract.Payload), time));
    }
}
