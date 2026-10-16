using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortPartialCopy
{
    internal static async Task ExecuteAsync(D2ImportSession session, D2ImportImage image, CancellationToken token)
    {
        var reference = new EntityRef(image.Captured.Control!.Partition,
            D2ImportCrashProtocol.Collection, D2ImportCrashProtocol.DocumentId);
        while (image.NextOrdinal < image.Handle.PageCount)
        {
            await D2ImportInstall.OneAsync(session, image, token);
            var installed = session.Target.Store.Read(view =>
                view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
            if (installed is not null)
            {
                await D2ImportReceipts.ImportedAsync(session, image);
                var marker = D2ImportAbortAssertions.Marker(session, image);
                await Assert.That(marker).IsNotNull();
                await Assert.That(marker!.MoveId).IsEqualTo(image.Captured.Control.MoveId);
                await Assert.That(marker.Partition).IsEqualTo(image.Captured.Control.Partition);
                return;
            }
        }
        throw new InvalidOperationException("No actual copied document was observed before the terminal Install.");
    }
}
