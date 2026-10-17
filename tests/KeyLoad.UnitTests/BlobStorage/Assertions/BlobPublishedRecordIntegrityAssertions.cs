using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedRecordIntegrityAssertions
{
    internal static async Task Refused(DatabaseEngine database, ZoneTreeStore store,
        BlobRangeIntegrityFixture fixture, int fault)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var operations = new BlobStorageOperations(database);
        var requests = new BlobReadRequest[]
        {
            new(fixture.Blob, fixture.Metadata.Revision, BlobRangeIntegrityFixture.CrossOffset,
                BlobRangeIntegrityFixture.RawPartBytes),
            new(fixture.Blob, fixture.Metadata.Revision, BlobRangeIntegrityFixture.PayloadBytes, 0)
        };
        foreach (var request in requests)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                    operations.Read(BlobRangeIntegrityFixture.Principal, request));
                await Assert.That(failure.Code).IsEqualTo(fault is 0 or 2
                    ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
                await Assert.That(failure.Message).IsEqualTo(fault is 0 or 2
                    ? BlobRangeIntegrityFixture.UnsupportedDetail : BlobRangeIntegrityFixture.CorruptionDetail);
                await BlobWholeFlowAssertions.Unchanged(store, position, image);
            }
        }
    }
}
