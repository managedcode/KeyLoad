using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferRepairRecoveryCut
{
    internal static async Task AssertAsync(ZoneTreeStore store, DatabaseEngine database, CommandRequest command, CommitStage stage)
    {
        var mutation = (AdvanceQueueTransferRepair)command.Mutations.Single();
        var source = RemoteTransferAttemptCrashScenario.Source;
        var cut = store.Read(view => (Record: view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(source, mutation.TransferId)),
            Counter: RemoteTransferStorage.RequireSourceCounter(view, source), Raw: view.ReadOwnedValue(RemoteTransferStorage.IntentKey(source, mutation.TransferId)),
            Outcome: view.ReadOwnedValue(KeySpace.PartitionOutcome(source.Partition, CrashFixtureValues.Principal, command.CommandId))));
        var record = cut.Record ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var state = record.Repairs ?? throw new InvalidOperationException(RemoteTransferRepairCrashProtocol.Missing);
        var committed = cut.Outcome is not null;
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(committed).IsTrue(); }
        var generation = committed ? RemoteTransferRepairCrashProtocol.RepairedGeneration : RemoteTransferRepairCrashProtocol.InitialGeneration;
        await Assert.That(state.AcceptPolicyGeneration).IsEqualTo(mutation.Stage == QueueTransferRepairStage.Accept
            ? generation : RemoteTransferRepairCrashProtocol.InitialGeneration);
        await Assert.That(state.CompleteGeneration).IsEqualTo(mutation.Stage == QueueTransferRepairStage.Complete
            ? generation : RemoteTransferRepairCrashProtocol.InitialGeneration);
        await Assert.That((long)state.History.Length).IsEqualTo(generation - RemoteTransferRepairCrashProtocol.InitialGeneration);
        await Assert.That(cut.Counter.StoredRecords).IsEqualTo(generation);
        await Assert.That(cut.Counter.StoredBytes).IsEqualTo(cut.Raw!.LongLength + record.ReceiptReservationBytes
            - Encoding.UTF8.GetByteCount(record.ReceiptToken ?? RemoteTransferRepairCrashProtocol.EmptyReceipt));
        await Assert.That(record.State).IsEqualTo(QueueTransferState.OutputPending);
        if (committed)
        {
            var reference = await Assert.That(state.History).HasSingleItem();
            await Assert.That(reference.WitnessToken).IsEqualTo(mutation.FailureWitness);
            await Assert.That(reference.Stage).IsEqualTo(mutation.Stage);
        }
        if (mutation.Stage == QueueTransferRepairStage.Accept)
        {
            await Assert.That(database.InspectQueueTransferReceipt(CrashFixtureValues.Principal,
                RemoteTransferAttemptCrashScenario.Destination, source, mutation.TransferId)).IsNull();
            await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, RemoteTransferAttemptCrashScenario.Destination,
                RemoteTransferAttemptCrashProtocol.OriginalMessage)).IsNull();
        }
    }
}
