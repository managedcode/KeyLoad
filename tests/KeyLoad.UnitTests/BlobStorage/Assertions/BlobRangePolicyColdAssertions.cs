using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangePolicyColdAssertions
{
    private const string Unavailable = "The credential is unavailable or expired.";

    internal static async Task Denied(DatabaseEngine database, ZoneTreeStore store,
        BlobRangeIntegrityFixture published, CancellationToken token)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var operations = new BlobStorageOperations(database);
        foreach (var request in Requests(published))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                    operations.Read(BlobRangePolicyColdTrial.Reader, request, token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
                await Assert.That(failure.Message).IsEqualTo(Unavailable);
                await BlobWholeFlowAssertions.Unchanged(store, position, image);
            }
        }
    }

    internal static async Task Healthy(DatabaseEngine database, ZoneTreeStore store,
        BlobRangeIntegrityFixture published, CancellationToken token)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var operations = new BlobStorageOperations(database);
        foreach (var request in Requests(published))
        {
            var read = operations.Read(BlobRangePolicyColdTrial.Reader, request, token);
            var expected = new BlobReadResult(published.Metadata, request.Offset,
                BlobRangeIntegrityFixture.Payload.AsMemory(checked((int)request.Offset), request.Count));
            await BlobWholeFlowAssertions.Exact(read, expected);
            await BlobWholeFlowAssertions.Unchanged(store, position, image);
        }
    }

    private static BlobReadRequest[] Requests(BlobRangeIntegrityFixture published)
        => [new(published.Blob, published.Metadata.Revision, BlobRangeIntegrityFixture.CrossOffset,
                BlobRangeIntegrityFixture.RawPartBytes),
            new(published.Blob, published.Metadata.Revision, BlobRangeIntegrityFixture.PayloadBytes, 0)];
}
