using KeyLoad.Core;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class TransactionalReadViewCommands
{
    internal static async Task SeedAsync(TransactionalReadViewState state)
    {
        var fixture = state.Harness;
        fixture.Configure(DocumentIndexCommittedScanOracle.Collection, ResourceKind.Collection,
            indexes: [new(DocumentIndexCommittedScanOracle.IndexName, [DocumentIndexCommittedScanOracle.LabelPath])]);
        fixture.Commit(new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.ReplaceId,
                DocumentIndexCommittedScanOracle.OriginalJson, 0L),
            new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.DeleteId,
                DocumentIndexCommittedScanOracle.DeletedJson, 0L),
            new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.StableId,
                DocumentIndexCommittedScanOracle.StableJson, 0L));
        state.Identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(state.Store);
        state.OldPosition = state.Store.Position;
        state.OldImage = QueueImage(state.Store);
        await DocumentIndexCommittedScanOracle.AssertOldCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(state.Store, fixture.Partition), state.OldPosition);
        var id = Guid.NewGuid();
        state.Writer = Operation(id, new(id, fixture.Partition,
            [new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.ReplaceId,
                DocumentIndexCommittedScanOracle.ReplacementJson, 1L, ExplicitReplacement: true),
             new DeleteDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.DeleteId, 1L),
             new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.InsertId,
                DocumentIndexCommittedScanOracle.InsertedJson, 0L)]));
        state.WriterReceipt = new(id, new(state.Identity.Incarnation, fixture.Partition.AtomicPartitionId, state.OldPosition + 1L, 1L),
            [new("putDocument", DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.ReplaceId, 2L),
             new("deleteDocument", DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.DeleteId, 2L),
             new("putDocument", DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.InsertId, 1L)], DurabilityProfile.ProcessDurable);
    }
    internal static ReplicatedOperation Operation(Guid id, CommandRequest request)
        => new(id, OperationKind.Batch, "root", default, System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));
    internal static string[] QueueImage(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store)
        => store.Read(view =>
        {
            var page = view.Scan([], TransactionalReadViewState.MaximumRows);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The transactional read-view original image exceeded its bound.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
        });
    internal static byte[] Outcome(TransactionalReadViewState state, ReplicatedOperation operation)
        => state.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Harness.Partition, "root", operation.Id)))
            ?? throw new InvalidOperationException("The original transactional read-view outcome is missing.");
}
