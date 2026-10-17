using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class TransactionalReadViewContinuation
{
    internal static async Task HealthyAsync(TransactionalReadViewState state, CancellationToken token)
    {
        var before = state.Store.Position;
        var id = Guid.NewGuid();
        state.Healthy = TransactionalReadViewCommands.Operation(id, new(id, state.Harness.Partition,
            [new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.HealthyId,
                DocumentIndexCommittedScanOracle.HealthyJson, 0L)]));
        state.HealthyReceipt = new(id, new(state.Identity.Incarnation, state.Harness.Partition.AtomicPartitionId, before + 1L, 1L),
            [new("putDocument", DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.HealthyId, 1L)], DurabilityProfile.ProcessDurable);
        var actual = state.Database.ApplyEmbedded(state.Healthy, token).Get<CommitReceipt>();
        state.HealthyOutcome = TransactionalReadViewCommands.Outcome(state, state.Healthy);
        state.FinalImage = TransactionalReadViewCommands.QueueImage(state.Store);
        state.FinalPosition = state.Store.Position;
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
    private static async Task ReplayAsync(TransactionalReadViewState state, ReplicatedOperation original,
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
