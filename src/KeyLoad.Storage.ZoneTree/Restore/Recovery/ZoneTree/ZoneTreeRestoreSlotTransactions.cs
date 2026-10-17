namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns actual native reconciliation and reset commits in the original admitted slot.</summary>
internal static class ZoneTreeRestoreSlotTransactions
{
    private const string Invalid = "The original restore slot cannot continue at its recovered native position.";

    internal static void Continue(ZoneTreeStore store, StoreIdentity original,
        ClusterRestoreSlotContext context, ReadOnlyMemory<byte> metadata,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>, ClusterRestoreSlotContext> reconcile,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify,
        Action<NativeClusterRestoreStage>? observer,
        ZoneTreeRestoreSlotConservation conservation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (store.Position == context.SourcePosition)
        {
            if (store.Identity.NodeId != original.NodeId || store.Identity.Incarnation != original.Incarnation
                || !store.Identity.SigningKey.Span.SequenceEqual(original.SigningKey.Span)
                || store.Identity.DispatchPaused != original.DispatchPaused)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            // Only this independently verified original position permits replacing historical slot rows.
            store.Commit((transaction, nextPosition) =>
            {
                ct.ThrowIfCancellationRequested();
                var proof = conservation.Capture(transaction, ct);
                var applied = ZoneTreeRestoreConservation.ReadApplied(transaction);
                reconcile(transaction, original, nextPosition, metadata, context);
                var row = ZoneTreeRestoreSlotState.Require(transaction, context,
                    ClusterRestoreSlotCommitKind.Reconciled, nextPosition);
                if (row.NativeCommitPosition != nextPosition
                    || transaction.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset()) is not null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                conservation.StageSource(transaction, original, context, row, proof, applied, ct);
                transaction.ValidateCommit();
                ct.ThrowIfCancellationRequested();
                return true;
            });
            observer?.Invoke(NativeClusterRestoreStage.Reconciled);
        }
        store.Read(view =>
        {
            var reconciled = ZoneTreeRestoreSlotState.Require(view, context,
                ClusterRestoreSlotCommitKind.Reconciled, store.Position);
            var reset = view.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset());
            if (reset is null && store.Position != reconciled.NativeCommitPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            if (reset is not null)
            {
                var actual = ZoneTreeRestoreSlotState.Require(view, context,
                    ClusterRestoreSlotCommitKind.AuthorityReset, store.Position);
                ZoneTreeRestoreSlotConservation.RequireSameOrigin(reconciled, actual);
                if (store.Position != actual.NativeCommitPosition
                    || actual.MappingsDigest != reconciled.MappingsDigest
                    )
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
            }
            conservation.Require(view, store.Identity, context, reconciled, reset is not null, ct);
            verify(view, store.Identity, store.Position, context);
            ct.ThrowIfCancellationRequested();
            return true;
        });
    }

    internal static void Reset(ZoneTreeStore store, ClusterRestoreSlotContext context,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, Action<NativeClusterRestoreStage>? observer,
        ZoneTreeRestoreSlotConservation conservation, CancellationToken ct)
    {
        var reset = store.Read(view => view.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset()));
        if (reset is not null)
        {
            store.Read(view =>
            {
                var reconciled = ZoneTreeRestoreSlotState.Require(view, context,
                    ClusterRestoreSlotCommitKind.Reconciled, store.Position);
                conservation.Require(view, store.Identity, context, reconciled, reset: true, ct);
                return true;
            });
            return;
        }
        store.Commit((transaction, nextPosition) =>
        {
            ct.ThrowIfCancellationRequested();
            var reconciled = ZoneTreeRestoreSlotState.Require(transaction, context,
                ClusterRestoreSlotCommitKind.Reconciled, store.Position);
            verify(transaction, store.Identity, store.Position, context);
            conservation.StageReset(transaction, store.Identity, context, reconciled, ct);
            ZoneTreeRestoreSlotState.Reset(transaction, context, reconciled, nextPosition, ct);
            return true;
        });
        observer?.Invoke(NativeClusterRestoreStage.AuthorityReset);
    }
}
