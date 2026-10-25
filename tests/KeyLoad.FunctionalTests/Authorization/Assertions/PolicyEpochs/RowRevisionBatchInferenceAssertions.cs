using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using P = KeyLoad.FunctionalTests.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RowRevisionBatchInferenceAssertions
{
    internal static async Task HealthyAsync(TestDatabase creation, DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        CommandRequest command, Result<object> actual, CommitToken expectedToken, long previousStorePosition, string kind, CancellationToken token)
    {
        var receipt = actual.Get<CommitReceipt>();
        CommitReceipt expected = new(command.CommandId, expectedToken, [new(P.PutReceipt, P.Collection, P.Own, P.RepairRevision),
                new(RowRevisionInferenceRequests.ReceiptKind(kind), P.Collection, P.Hidden, P.HealthyRevision)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(previousStorePosition + 1L);
        await CurrentAsync(creation, database, partition, kind, token);
    }

    internal static async Task CurrentAsync(TestDatabase creation, DatabaseEngine database, PartitionRef partition,
        string kind, CancellationToken token)
    {
        await RowRevisionInferenceAssertions.DocumentAsync(creation, database, partition, P.Own,
            P.RepairRevision, P.Changed, token);
        if (kind == P.Delete)
        {
            await Assert.That(database.GetDocument(P.Root, new(partition, P.Collection, P.Hidden), cancellationToken: token)).IsNull();
        }
        else
        {
            await RowRevisionInferenceAssertions.DocumentAsync(creation, database, partition, P.Hidden,
                P.HealthyRevision, P.Changed, token);
        }
    }
}
