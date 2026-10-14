namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRestoreSlotCompletionObservation
{
    private const string TerminalCutLost = "The published restore target no longer has its original terminal native cut.";

    internal static NativeCatalogRestoreSlotCompletion Observe(ZoneTreeStore store, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, CancellationToken ct)
        => store.Read(view =>
        {
            ct.ThrowIfCancellationRequested();
            ZoneTreeRestoreSlotState.RequireTarget(store.Identity, context, signingKey);
            var reconciled = ZoneTreeRestoreSlotState.Require(view, context,
                ClusterRestoreSlotCommitKind.Reconciled, store.Position);
            var reset = ZoneTreeRestoreSlotState.Require(view, context,
                ClusterRestoreSlotCommitKind.AuthorityReset, store.Position);
            if (reset.MappingsDigest != reconciled.MappingsDigest)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeRestoreSlotDriver.Invalid);
            }
            if (reset.NativeCommitPosition != store.Position)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, TerminalCutLost);
            }
            verify(view, store.Identity, store.Position, context);
            return new NativeCatalogRestoreSlotCompletion(NativeCatalogRestoreSlotCompletion.CurrentVersion,
                context, store.Identity.NodeId, store.Identity.Incarnation, context.TargetSignerFingerprint,
                reconciled.NativeCommitPosition, reset.NativeCommitPosition, store.Position);
        });
}
