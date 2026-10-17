using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionAssertions
{
    private const int CompleteFixtureScanLimit = 4096;

    internal static string[] State(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], CompleteFixtureScanLimit);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete tenant fixture exceeded its finite oracle.");
        }

        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task RecordAsync(DatabaseEngine database, ReplicatedOperation operation,
        PartitionRef partition, string id)
    {
        var record = database.GetDocument(operation.PrincipalId,
            new(partition, TenantAdmissionOperations.Collection, id));
        ArgumentNullException.ThrowIfNull(record);
        var reference = new EntityRef(partition, TenantAdmissionOperations.Collection, id);
        await Assert.That(record.Reference).IsEqualTo(reference);
        await Assert.That(record.Revision).IsEqualTo(1L);
        await Assert.That(record.Json).IsEqualTo(TenantAdmissionOperations.Body);
        await Assert.That(record.Redacted).IsFalse();
        await Assert.That(record.RedactedFields).IsEmpty();
        var stored = database.Store.Read(view => global::KeyLoad.Storage.StorageRecords.GetRecord<DocumentRecord>(view,
            DocumentStorageKeys.RecordKey(reference)));
        ArgumentNullException.ThrowIfNull(stored);
        await Assert.That(stored.Reference).IsEqualTo(reference);
        await Assert.That(stored.Revision).IsEqualTo(1L);
        await Assert.That(stored.Json).IsEqualTo(TenantAdmissionOperations.Body);
        await Assert.That(stored.UpdatedAt).IsEqualTo(operation.EvaluatedAt);
        await Assert.That(stored.Deleted).IsFalse();
        ArgumentNullException.ThrowIfNull(stored.Access);
        await Assert.That(stored.Access.OwnerId).IsNull();
        await Assert.That(stored.Access.ProjectId).IsNull();
        await Assert.That(stored.Access.TeamId).IsNull();
    }

    internal static async Task ReceiptAsync(OperationResult result, ReplicatedOperation operation,
        string id, CommitToken expectedToken)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.Token).IsEqualTo(expectedToken);
        var mutation = receipt.Mutations.Single();
        await Assert.That(mutation.Kind).IsEqualTo("putDocument");
        await Assert.That(mutation.Resource).IsEqualTo(TenantAdmissionOperations.Collection);
        await Assert.That(mutation.Id).IsEqualTo(id);
        await Assert.That(mutation.Revision).IsEqualTo(1L);
        await Assert.That(mutation.CompositionReferences).IsEmpty();
    }

    internal static async Task UnchangedAsync(TestDatabase fixture, string[] state,
        (long LastIndex, long CommittedIndex, long Term) journal)
    {
        await Assert.That(State(fixture.Store).SequenceEqual(state)).IsTrue();
        await Assert.That(fixture.ReadJournalCut()).IsEqualTo(journal);
    }
    internal static async Task<ReplicatedOperation> CommittedAsync(TestDatabase fixture, ReplicatedOperation issued)
    {
        var committed = fixture.ReadRetainedEntry(issued.Id).Operation;
        ArgumentNullException.ThrowIfNull(committed);
        await Assert.That(committed.Id).IsEqualTo(issued.Id);
        await Assert.That(committed.PrincipalId).IsEqualTo(issued.PrincipalId);
        await Assert.That(committed.PayloadJson).IsEqualTo(issued.PayloadJson);
        return committed;
    }
}
