using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RowRevisionBatchInferenceAssertions
{
    internal static async Task HealthyAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        CommandRequest command, Result<object> actual, string kind, CancellationToken token)
    {
        var receipt = actual.Get<CommitReceipt>();
        CommitReceipt expected = new(command.CommandId, new(store.Identity.Incarnation, partition.AtomicPartitionId, store.Position), [new(P.PutReceipt, P.Collection, P.Own, P.RepairRevision),
                new(RowRevisionInferenceRequests.ReceiptKind(kind), P.Collection, P.Hidden, P.HealthyRevision)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await CurrentAsync(database, partition, kind, token);
    }

    internal static async Task CurrentAsync(DatabaseEngine database, PartitionRef partition,
        string kind, CancellationToken token)
    {
        await RowRevisionInferenceAssertions.DocumentAsync(database, partition, P.Own,
            P.RepairRevision, P.Changed, token);
        if (kind == P.Delete)
        {
            await Assert.That(database.GetDocument(P.Root, new(partition, P.Collection, P.Hidden), cancellationToken: token)).IsNull();
        }
        else
        {
            await RowRevisionInferenceAssertions.DocumentAsync(database, partition, P.Hidden,
                P.HealthyRevision, P.Changed, token);
        }
    }
}
