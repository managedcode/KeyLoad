using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferAttemptRecoveryCut
{
    internal static async Task AssertAsync(ZoneTreeStore store, DatabaseEngine database, CommandRequest command, CommitStage stage)
    {
        var mutation = (AdvanceQueueTransferAttempt)command.Mutations.Single();
        var source = RemoteTransferAttemptCrashScenario.Source;
        var cut = store.Read(view => (Record: view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(source, mutation.TransferId)),
            Counter: RemoteTransferStorage.RequireSourceCounter(view, source),
            Raw: view.ReadOwnedValue(RemoteTransferStorage.IntentKey(source, mutation.TransferId)),
            Outcome: view.ReadOwnedValue(KeySpace.PartitionOutcome(source.Partition, CrashFixtureValues.Principal, command.CommandId))));
        var state = cut.Record!.Attempts ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        var committed = cut.Outcome is not null;
        var raw = cut.Raw ?? throw new InvalidOperationException(RemoteTransferAttemptCrashProtocol.Missing);
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(committed).IsTrue(); }
        var generation = committed ? RemoteTransferAttemptCrashProtocol.AdvancedGeneration : RemoteTransferAttemptCrashProtocol.OriginalGeneration;
        await Assert.That(state.Generation).IsEqualTo(generation);
        await Assert.That(cut.Counter.StoredRecords).IsEqualTo(generation);
        await Assert.That((long)state.History.Length).IsEqualTo(generation - RemoteTransferAttemptCrashProtocol.OriginalGeneration);
        await Assert.That(cut.Counter.StoredBytes).IsEqualTo(raw.LongLength + cut.Record.ReceiptReservationBytes - System.Text.Encoding.UTF8.GetByteCount(cut.Record.ReceiptToken ?? RemoteTransferAttemptCrashProtocol.EmptyToken));
        await Assert.That(cut.Record.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(database.InspectQueueTransferReceipt(CrashFixtureValues.Principal,
            RemoteTransferAttemptCrashScenario.Destination, source, mutation.TransferId)).IsNull();
        await Assert.That(database.InspectMessage(CrashFixtureValues.Principal, RemoteTransferAttemptCrashScenario.Destination,
            RemoteTransferAttemptCrashProtocol.OriginalMessage)).IsNull();
        if (committed)
        {
            var reference = await Assert.That(state.History).HasSingleItem();
            await Assert.That(reference.WitnessToken).IsEqualTo(mutation.FailureWitness);
            await Assert.That(reference.Generation).IsEqualTo(mutation.ExpectedGeneration);
        }
    }
}
