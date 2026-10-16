using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveRestoreSlots
{
    internal static NativeCatalogRestoreSlotCompletion Restore(D2ArchiveSlot slot,
        ReadExecutionBudget work, CancellationToken token, D2ArchiveAttack attack = D2ArchiveAttack.None)
        => ZoneTreeStore.RestoreCatalogBackupSlot(slot.Archive.Directory, slot.Destination, slot.Stage,
            UnitExecutionOptions.StorageExecution(), TimeProvider.System, slot.Context, slot.SigningKey,
            (transaction, identity, nextPosition, metadata, context) =>
            {
                _ = ClusterRestoreCatalogReconciliation.ReconcileSlot(transaction, identity, context.SourcePosition,
                    nextPosition, metadata, D2ArchiveData.Credential, slot.Cuts, slot.Mappings, context,
                    UnitExecutionOptions.DatabaseLimits(), TimeProvider.System, work);
                D2ArchiveFaults.Alter(transaction, slot.Archive.Source, attack);
            },
            (view, identity, position, context) => Verify(slot, view, identity, position, context, work, token),
            null, token);

    internal static NativeCatalogRestoreSlotCompletion Observe(D2ArchiveSlot slot, ReadExecutionBudget work, CancellationToken token)
        => ZoneTreeStore.ObserveCatalogBackupSlot(slot.Archive.Directory, slot.Destination,
            UnitExecutionOptions.StorageExecution(), TimeProvider.System, slot.Context, slot.SigningKey,
            (view, identity, position, context) => Verify(slot, view, identity, position, context, work, token), token);

    private static void Verify(D2ArchiveSlot slot, IKeyValueView target, StoreIdentity identity, long position,
        ClusterRestoreSlotContext context, ReadExecutionBudget work, CancellationToken token)
        => _ = ZoneTreeStore.ReadVerifiedCatalogBackup(slot.Archive.Directory, UnitExecutionOptions.StorageExecution(),
            (source, originalIdentity, originalPosition, metadata) =>
            {
                if (originalPosition != context.SourcePosition)
                {
                    throw new InvalidOperationException("The verified archive cut changed.");
                }
                ClusterRestoreImageComparison.Require(source, originalIdentity, metadata, target, identity, position,
                    D2ArchiveData.Credential, slot.Cuts, slot.Mappings, context, UnitExecutionOptions.DatabaseLimits(),
                    TimeProvider.System, work);
                return true;
            }, token);
}
