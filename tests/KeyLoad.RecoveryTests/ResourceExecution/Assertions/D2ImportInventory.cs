using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportInventory
{
    internal static async Task CheckpointAsync(D2ImportSession session, D2ImportImage image, bool present)
    {
        var control = image.Captured.Control!;
        var actual = session.Target.Store.Read(view => ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryImportOriginV1>(
            view, ResourceOwnerInventoryMovementRows.ImportKey(control.Partition, control.MoveId),
            session.Target.Database.Limits));
        await Assert.That(actual is not null).IsEqualTo(present);
        if (present)
        {
            await Assert.That(actual!.MoveId).IsEqualTo(control.MoveId);
            await Assert.That(actual.Partition).IsEqualTo(control.Partition);
            await Assert.That(actual.PriorOrigin).IsNotNull();
        }
    }

    internal static async Task BlockedAsync(D2ImportSession session, CancellationToken token)
    {
        var sourceRows = D2InventoryRecoveryPages.Image(session.Source);
        var rows = D2InventoryRecoveryPages.Image(session.Target);
        var position = session.Target.Store.Position;
        var index = session.Target.Journal.Log.State.LastIndex;
        try
        {
            _ = Capture(session, token);
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.RecoveryRequired)
        {
            await Assert.That(session.Target.Store.Position).IsEqualTo(position);
            await Assert.That(session.Target.Journal.Log.State.LastIndex).IsEqualTo(index);
            await Assert.That(D2InventoryRecoveryPages.Image(session.Target).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
            await Assert.That(D2InventoryRecoveryPages.Image(session.Source).SequenceEqual(sourceRows, StringComparer.Ordinal)).IsTrue();
            return;
        }
        throw new InvalidOperationException("An active native import did not refuse the local inventory without effects.");
    }

    internal static async Task CompleteAsync(D2ImportSession session, CancellationToken token)
    {
        var result = Capture(session, token);
        var actual = result.Entries.OrderBy(row => row.Partition.PartitionKey, StringComparer.Ordinal).ToArray();
        var expected = new[] { D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.LocalSecond };
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var ordinal = D2ImportCrashProtocol.Empty; ordinal < expected.Length; ordinal++)
        {
            var row = actual[ordinal];
            await Assert.That(row.Partition).IsEqualTo(D2ImportCrashProtocol.LocalPartition(expected[ordinal]));
            await D2InventoryRecoveryPages.SameAsync(result.Control.Lifetime, row.Lifetime);
            await D2InventoryRecoveryPages.SameAsync(session.Owners.Destination.Owner, row.ObservedOwner);
            await D2InventoryRecoveryPages.SameAsync(session.Owners.Destination, row.CurrentRoute);
            await Assert.That(row.CurrentPlacement.PhysicalShardId).IsEqualTo(session.Owners.Destination.Owner.PhysicalShardId);
            await Assert.That(row.CurrentPlacement.Incarnation).IsEqualTo(session.Owners.Destination.Owner.Incarnation);
        }
    }

    private static (ResourceOwnerInventoryControlV1 Control, ResourceOwnerInventoryEntryV1[] Entries) Capture(
        D2ImportSession session, CancellationToken token)
    {
        var target = session.Target;
        var work = D2InventoryRecoveryPages.Work(target, token);
        var request = target.Database.AdmitResourceOwnerInventoryRequest(D2ImportCrashProtocol.Principal,
            Guid.NewGuid(), D2ImportCrashProtocol.Tenant, D2ImportCrashProtocol.Database,
            D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.Single, session.ExpiresAt, work);
        var control = target.Database.CaptureResourceOwnerInventoryControl(D2ImportCrashProtocol.Principal, request, work);
        ResourceOwnerInventoryCursorV1? cursor = null;
        ResourceOwnerInventoryOwnerCutV1? cut = null;
        var entries = new List<ResourceOwnerInventoryEntryV1>();
        do
        {
            var page = target.Database.ReadResourceOwnerInventoryPage(D2ImportCrashProtocol.Principal,
                control, session.Owners.Destination, cursor, work);
            cut ??= page.Cut;
            if (entries.Count + page.Entries.Length > D2ImportCrashProtocol.Pair)
            {
                throw new InvalidOperationException("The literal two-partition recovery inventory exceeded its bound.");
            }
            entries.AddRange(page.Entries);
            cursor = page.Continuation;
        }
        while (cursor is not null);
        target.Database.ValidateResourceOwnerInventoryOwner(D2ImportCrashProtocol.Principal, control, cut!, work);
        target.Database.ValidateResourceOwnerInventoryControl(D2ImportCrashProtocol.Principal, control, work);
        return (control, entries.ToArray());
    }
}
