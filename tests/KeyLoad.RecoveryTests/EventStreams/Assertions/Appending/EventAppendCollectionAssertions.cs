using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendCollectionAssertions
{
    internal static async Task<EntityRef> ReferenceAsync(IAtomicStore store, string id)
    {
        var partition = EventAppendCrashContract.Partition;
        var commandId = EventAppendCrashScenario.CollectionCommandId;
        var stored = store.Read(view => view.GetRecord<StoredOutcome>(
            OutcomeStoreOracle.GlobalKey(EventAppendCrashContract.Principal, commandId)))
            ?? throw new InvalidOperationException("The original collection creation outcome is missing.");
        var original = stored.PreparedCommand;
        var definition = new ResourceDefinition(EventAppendCrashContract.Documents, ResourceKind.Collection,
            partition.TransactionDomainId)
        {
            Indexes = [new(EventAppendCrashContract.DocumentIndex, [EventAppendCrashContract.DocumentIndexPath])]
        };
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await Assert.That(original.CommandId).IsEqualTo(commandId);
        await Assert.That(original.UserId).IsEqualTo(EventAppendCrashContract.Principal);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(stored.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(Errors.Code(stored.Result.Problem)).IsNull();
        await Assert.That(JsonDefaults.Serialize(stored.Result.Get<ResourceDefinition>()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(definition))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(OrleansSerialization.Deserialize<ConfigureResourceRequest>(original.Value.Input.Span))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        var fingerprint = DatabaseCommandFingerprint.Compute(commandId, OperationKind.ConfigureResource,
            EventAppendCrashContract.Principal, JsonSerializer.Serialize(request, JsonDefaults.Options));
        await Assert.That(original.Value.Fingerprint).IsEqualTo(fingerprint);
        await Assert.That(stored.Fingerprint).IsEqualTo(fingerprint);
        var lifetime = ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
        return new(partition, EventAppendCrashContract.Documents, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }
}
