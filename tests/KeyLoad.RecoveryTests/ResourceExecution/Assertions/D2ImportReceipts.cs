using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportReceipts
{
    internal static async Task<CommitReceipt> WriteAsync(D2ImportSession session, ReplicatedOperation original,
        string partition, long index, CancellationToken token)
    {
        var actual = session.Target.Journal.Submit(original, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(original.Id,
            new(session.Owners.Destination.Owner.Incarnation,
                D2ImportCrashProtocol.LocalPartition(partition).AtomicPartitionId, index,
                session.Owners.Destination.Owner.PlacementEpoch),
            [new(D2ImportCrashProtocol.PutKind, D2ImportCrashProtocol.LocalCollection,
                D2ImportCrashProtocol.DocumentId, D2ImportCrashProtocol.Single)], DurabilityProfile.ProcessDurable);
        await D2InventoryRecoveryPages.SameAsync(expected, actual);
        return actual;
    }

    internal static async Task LocalDocumentAsync(D2ImportSession session, ReplicatedOperation operation,
        string partition, string json)
    {
        var reference = new EntityRef(D2ImportCrashProtocol.LocalPartition(partition),
            D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.DocumentId)
        {
            Lifetime = new(session.Lifetimes.Local.Incarnation.Value)
        };
        var actual = session.Target.Database.GetDocument(D2ImportCrashProtocol.Principal, reference);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(reference);
        await Assert.That(actual.Json).IsEqualTo(json);
        await Assert.That(actual.Revision).IsEqualTo(D2ImportCrashProtocol.Single);
        var persisted = session.Target.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(reference)));
        await Assert.That(persisted).IsNotNull();
        await D2InventoryRecoveryPages.SameAsync(new DocumentRecord(reference, D2ImportCrashProtocol.Single,
            json, new(), operation.EvaluatedAt), persisted!);
    }

    internal static async Task ImportedAsync(D2ImportSession session, D2ImportImage image)
    {
        var partition = image.Captured.Control!.Partition;
        var reference = new EntityRef(partition, D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.DocumentId)
        {
            Lifetime = new(session.Lifetimes.Source.Incarnation.Value)
        };
        var seedIndex = partition.PartitionKey == D2ImportCrashProtocol.FirstPartition
            ? D2ImportCrashProtocol.Empty : D2ImportCrashProtocol.Single;
        var original = session.SourceSeeds[seedIndex].Original;
        var document = session.Target.Store.Read(view =>
            view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
        await Assert.That(document).IsNotNull();
        await D2InventoryRecoveryPages.SameAsync(new DocumentRecord(reference, D2ImportCrashProtocol.Single,
            D2ImportCrashProtocol.SourceJson, new(), original.EvaluatedAt), document!);
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeNode node,
        IEnumerable<(ReplicatedOperation Original, OperationResult Result)> operations, CancellationToken token)
    {
        var rows = D2InventoryRecoveryPages.Image(node);
        var position = node.Store.Position;
        var state = node.Journal.Log.State;
        foreach (var observed in operations)
        {
            await D2InventoryRecoveryPages.SameAsync(observed.Result, node.Journal.Submit(observed.Original, token));
        }
        await Assert.That(node.Store.Position).IsEqualTo(position);
        await D2InventoryRecoveryPages.SameAsync(state, node.Journal.Log.State);
        await Assert.That(D2InventoryRecoveryPages.Image(node).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
    }
}
