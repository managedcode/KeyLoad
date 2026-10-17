using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobSupersessionAssertions
{
    internal static async Task RequireAsync(DatabaseEngine owner, ZoneTreeStore store,
        BlobRangeIntegrityFixture first, BlobSupersessionVersion second, BlobState originalState,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var reads = new BlobStorageOperations(owner);
        await BlobWholeFlowAssertions.Exact(reads.Metadata("root", new(first.Blob), token), second.Metadata);
        var full = reads.Read("root", new(first.Blob, 2L, 0, BlobSupersessionPublication.Payload.Length), token);
        await BlobWholeFlowAssertions.Exact(full, new BlobReadResult(second.Metadata, 0, BlobSupersessionPublication.Payload));
        var empty = reads.Read("root", new(first.Blob, 2L, BlobSupersessionPublication.Payload.Length, 0), token);
        await BlobWholeFlowAssertions.Exact(empty, new BlobReadResult(second.Metadata, BlobSupersessionPublication.Payload.Length,
            ReadOnlyMemory<byte>.Empty));
        await RetainedAsync(owner, first, originalState);
        await BlobWholeFlowAssertions.Quotas(owner, first.Blob,
            (long)BlobRangeIntegrityFixture.PayloadBytes + BlobSupersessionPublication.Payload.Length, 2, 0);
        foreach (var command in first.Commands.Concat(second.Commands))
        {
            var replay = owner.ApplyEmbedded(BlobPublishedLifetimeOperations.Original(command), cancellationToken: token);
            await BlobWholeFlowAssertions.Exact(replay, command.Original);
            await BlobWholeFlowAssertions.Unchanged(store, position, image);
        }
        foreach (var count in new[] { 1, 0 })
        {
            var stale = Assert.ThrowsExactly<KeyLoadException>(() => reads.Read("root",
                new(first.Blob, 1L, 0, count), token));
            await Assert.That(stale.Code).IsEqualTo(ErrorCode.RevisionConflict);
            await Assert.That(stale.Message).IsEqualTo("The blob head revision changed.");
            await BlobWholeFlowAssertions.Unchanged(store, position, image);
        }
        await BlobWholeFlowAssertions.Exact(reads.Read("root",
            new(first.Blob, 2L, 0, BlobSupersessionPublication.Payload.Length), token), full);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task RetainedAsync(DatabaseEngine owner, BlobRangeIntegrityFixture first, BlobState original)
    {
        var retained = owner.Store.Read(view => BlobRecordReader.Get<BlobState>(view,
            BlobKeys.State(first.Blob, first.UploadId)));
        await BlobWholeFlowAssertions.Exact(retained, original with
        {
            Retired = true
        });
        for (var ordinal = 0; ordinal < BlobRangeIntegrityFixture.Parts; ordinal++)
        {
            var key = BlobRangeIntegrityOracle.Key(BlobRangeIntegrityFixture.PartSpace, first, ordinal);
            var bytes = owner.Store.Read(view => view.ReadOwnedValue(key));
            var expected = BlobRangeIntegrityFixture.Payload.AsMemory(ordinal * BlobRangeIntegrityFixture.RawPartBytes,
                ordinal == 0 ? BlobRangeIntegrityFixture.RawPartBytes : BlobRangeIntegrityFixture.TailBytes);
            await Assert.That(bytes).IsNotNull();
            await Assert.That(bytes!.AsSpan().SequenceEqual(expected.Span)).IsTrue();
            var metadata = owner.Store.Read(view => BlobRecordReader.Get<BlobPartMetadata>(view,
                BlobKeys.PartMeta(first.Blob, first.UploadId, ordinal)));
            await BlobWholeFlowAssertions.Exact(metadata, new BlobPartMetadata(BlobKeys.FormatVersion,
                expected.Length, BlobIntegrity.PartHash(expected.Span)));
        }
    }
}
