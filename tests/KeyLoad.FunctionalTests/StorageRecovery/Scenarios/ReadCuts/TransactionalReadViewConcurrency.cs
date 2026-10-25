using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class TransactionalReadViewConcurrency
{
    internal static async Task RunAsync(TransactionalReadViewState state, CancellationToken token)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ZoneTreeReadCutLease? lease = null;
        Task<TransactionalReadViewObserved>? scan = null;
        Task<CommitReceipt>? writer = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lease = state.Capture(token);
            await TransactionalReadViewValues.CutAsync(state, lease.Cut, state.OldPosition);
            var original = lease;
            scan = Task.Run(() => ReadHeld(original, entered, release, token));
            if (!entered.Wait(TransactionalReadViewState.Deadline, token))
            {
                throw new TimeoutException("The original execution snapshot never reached its first delivered row.");
            }
            writer = Task.Run(() => state.Database.ApplyEmbedded(state.Writer, token).Get<CommitReceipt>());
            var receipt = await writer.WaitAsync(TransactionalReadViewState.Deadline, TimeProvider.System, token);
            state.WriterOutcome = TransactionalReadViewCommands.Outcome(state, state.Writer);
            await TransactionalReadViewValues.ReceiptAsync(state.WriterReceipt, receipt);
            await DocumentIndexCommittedScanOracle.AssertCommittedCutAsync(
                DocumentIndexCommittedScanOracle.ReadCut(state.Store, state.Harness.Partition), state.OldPosition + 1L);
            await TransactionalReadViewRefusals.WhileHeldAsync(state, original);
        }, failures);
        ServerFailureObserver.Observe(release.Set, failures);
        if (writer is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () => { await writer; }, failures);
        }
        if (scan is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var observed = await scan;
                await Assert.That(observed.Rows).IsEquivalentTo(state.OldImage, CollectionOrdering.Matching);
                await Assert.That(observed.Result.Records).IsEqualTo(state.OldImage.Length);
                await Assert.That(observed.Result.RuntimeAdvances).IsEqualTo((long)state.OldImage.Length + 1L);
                await Assert.That(observed.Result.HasMore).IsFalse();
                await Assert.That(observed.Result.StoppedByVisitor).IsFalse();
            }, failures);
        }
        if (lease is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewValues.CutAsync(state, lease.Cut, state.OldPosition), failures);
            ServerFailureObserver.Observe(lease.Dispose, failures);
            await ServerFailureObserver.ObserveAsync(() => TransactionalReadViewRefusals.DisposedAsync(lease), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static TransactionalReadViewObserved ReadHeld(ZoneTreeReadCutLease lease, ManualResetEventSlim entered,
        ManualResetEventSlim release, CancellationToken token)
    {
        var rows = new List<string>();
        var result = lease.VisitPrefix([], (key, value) =>
        {
            rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
            if (rows.Count == 1)
            {
                entered.Set();
                if (!release.Wait(TransactionalReadViewState.Deadline, token))
                {
                    throw new TimeoutException("The original execution snapshot visitor was not released.");
                }
            }
            return true;
        });
        return new(rows.ToArray(), result);
    }
}
