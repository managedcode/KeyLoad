using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ResourceExecution;

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

    internal static async Task RecordAsync(TestDatabase fixture, DatabaseEngine database, Command<DatabaseCommand> operation,
        PartitionRef partition, string id)
    {
        var record = database.GetDocument(operation.UserId!,
            new(partition, TenantAdmissionOperations.Collection, id));
        ArgumentNullException.ThrowIfNull(record);
        var reference = fixture.ExpectedReference(new(partition, TenantAdmissionOperations.Collection, id));
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
        await Assert.That(stored.UpdatedAt).IsEqualTo(new DateTimeOffset(operation.Timestamp));
        await Assert.That(stored.Deleted).IsFalse();
        ArgumentNullException.ThrowIfNull(stored.Access);
        await Assert.That(stored.Access.OwnerId).IsNull();
        await Assert.That(stored.Access.ProjectId).IsNull();
        await Assert.That(stored.Access.TeamId).IsNull();
    }

    internal static async Task ReceiptAsync(Result<object> result, Command<DatabaseCommand> operation,
        string id, CommitToken expectedToken)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await Assert.That(result.Problem?.Detail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(1);
        await Assert.That(receipt.Token).IsEqualTo(expectedToken);
        var mutation = receipt.Mutations.Single();
        await Assert.That(mutation.Kind).IsEqualTo("putDocument");
        await Assert.That(mutation.Resource).IsEqualTo(TenantAdmissionOperations.Collection);
        await Assert.That(mutation.Id).IsEqualTo(id);
        await Assert.That(mutation.Revision).IsEqualTo(1L);
        await Assert.That(mutation.CompositionReferences).IsEmpty();
    }

    internal static async Task UnchangedAsync(TestDatabase fixture, string[] state,
        (long AppliedPosition, long StorePosition) journal)
    {
        await Assert.That(State(fixture.Store).SequenceEqual(state)).IsTrue();
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(journal);
    }
    internal static async Task<Command<DatabaseCommand>> CommittedAsync(TestDatabase fixture, Command<DatabaseCommand> issued)
    {
        var committed = fixture.ReadStoredOutcome(issued.CommandId).PreparedCommand;
        ArgumentNullException.ThrowIfNull(committed);
        await Assert.That(committed.CommandId).IsEqualTo(issued.CommandId);
        await Assert.That(committed.UserId!).IsEqualTo(issued.UserId!);
        await Assert.That(committed.Value!.Input.Span.SequenceEqual(issued.Value!.Input.Span)).IsTrue();
        await Assert.That(committed.Value.Fingerprint).IsEqualTo(issued.Value.Fingerprint);
        return committed;
    }
}
