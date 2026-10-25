using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class TransactionalReadViewContinuation
{
    internal static async Task HealthyAsync(TransactionalReadViewState state, CancellationToken token)
    {
        var before = state.Database.LastApplied;
        await Assert.That(before).IsEqualTo(state.OldAppliedPosition + 1L);
        var id = Guid.NewGuid();
        state.Healthy = TransactionalReadViewCommands.Operation(state.Database, id, new(id, state.Harness.Partition,
            [new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.HealthyId,
                DocumentIndexCommittedScanOracle.HealthyJson, 0L)]));
        state.HealthyReceipt = new(id, new(state.Identity.Incarnation, state.Harness.Partition.AtomicPartitionId, before + 1L), [new("putDocument", DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.HealthyId, 1L)], DurabilityProfile.ProcessDurable, 1);
        var actual = state.Database.ApplyEmbedded(state.Healthy, token).Get<CommitReceipt>();
        state.HealthyOutcome = TransactionalReadViewCommands.Outcome(state, state.Healthy);
        state.FinalImage = TransactionalReadViewCommands.QueueImage(state.Store);
        state.FinalPosition = state.Store.Position;
        await Assert.That(state.FinalPosition).IsEqualTo(state.OldPosition + 2L);
        await TransactionalReadViewValues.ReceiptAsync(state.HealthyReceipt, actual);
        await RequireAsync(state, token);
    }
    internal static async Task RequireAsync(TransactionalReadViewState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DocumentIndexCommittedScanOracle.AssertHealthyCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(state.Store, state.Harness.Partition), state.FinalPosition), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplayAsync(state, state.Writer, state.WriterReceipt, state.WriterOutcome, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplayAsync(state, state.Healthy, state.HealthyReceipt, state.HealthyOutcome, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewValues.ConservedAsync(state, state.FinalImage, state.FinalPosition), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ReplayAsync(TransactionalReadViewState state, Command<DatabaseCommand> original,
        CommitReceipt receipt, byte[] bytes, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await TransactionalReadViewValues.ReceiptAsync(receipt,
            state.Database.ApplyEmbedded(original, token).Get<CommitReceipt>()), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(TransactionalReadViewCommands.Outcome(state, original)
            .AsSpan().SequenceEqual(bytes)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewValues.ConservedAsync(state, state.FinalImage, state.FinalPosition), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
