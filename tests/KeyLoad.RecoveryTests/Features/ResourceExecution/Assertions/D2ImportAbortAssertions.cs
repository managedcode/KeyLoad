using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortAssertions
{
    internal static ResourceOwnerInventoryImportOriginV1? Marker(D2ImportSession session, D2ImportImage image)
    {
        var control = image.Captured.Control!;
        return session.Target.Store.Read(view => ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryImportOriginV1>(
            view, ResourceOwnerInventoryMovementRows.ImportKey(control.Partition, control.MoveId), session.Target.Database.Limits));
    }

    internal static async Task MarkerAsync(D2ImportSession session, D2ImportImage image,
        ResourceOwnerInventoryImportOriginV1? expected)
    {
        var actual = Marker(session, image);
        if (expected is null)
        {
            await Assert.That(actual).IsNull();
            return;
        }
        await Assert.That(actual).IsNotNull();
        await D2InventoryRecoveryPages.SameAsync(expected, actual!);
    }

    internal static async Task OriginAsync(D2ImportSession session, bool unknown)
    {
        var origin = session.Target.Store.Read(view =>
            ResourceOwnerInventoryCatalog.ReadOrigin(view, session.Target.Database.Limits));
        await Assert.That(origin is { Proven: true }).IsEqualTo(!unknown);
        if (!unknown)
        {
            await Assert.That(origin!.StoreIncarnation).IsEqualTo(session.Target.Store.Identity.Incarnation);
        }
    }

    internal static async Task InventoryAsync(D2ImportSession session, bool unknown, CancellationToken token)
    {
        await OriginAsync(session, unknown);
        if (unknown)
        {
            await D2ImportInventory.BlockedAsync(session, token);
        }
        else
        {
            await D2ImportInventory.CompleteAsync(session, token);
        }
    }

    internal static async Task DocumentAbsentAsync(D2ImportSession session, D2ImportImage image)
    {
        var reference = new EntityRef(image.Captured.Control!.Partition,
            D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.DocumentId);
        var document = session.Target.Store.Read(view =>
            view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
        await Assert.That(document).IsNull();
    }

    internal static async Task SourceDocumentsAsync(D2ImportSession session)
    {
        for (var ordinal = D2ImportCrashProtocol.Empty; ordinal < D2ImportCrashProtocol.Pair; ordinal++)
        {
            var partition = D2ImportCrashProtocol.Partition(ordinal == D2ImportCrashProtocol.Empty
                ? D2ImportCrashProtocol.FirstPartition : D2ImportCrashProtocol.SecondPartition);
            var reference = new EntityRef(partition, D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.DocumentId);
            var document = session.Source.Store.Read(view =>
                view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
            var original = session.SourceSeeds[ordinal].Original;
            await D2InventoryRecoveryPages.SameAsync(new DocumentRecord(reference, D2ImportCrashProtocol.Single,
                D2ImportCrashProtocol.SourceJson, new(), original.EvaluatedAt), document);
        }
    }
}
