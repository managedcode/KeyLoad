using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangeIntegrityAssertions
{
    internal static async Task NativeState(DatabaseEngine database, BlobRangeIntegrityFixture fixture)
    {
        var incarnation = database.Store.Identity.Incarnation;
        var head = new BlobHead(BlobRangeIntegrityFixture.FormatVersion, incarnation, fixture.Metadata);
        var state = new BlobState(BlobRangeIntegrityFixture.FormatVersion, incarnation, incarnation,
            fixture.BeginCommandId, BlobRangeIntegrityFixture.Principal, fixture.Blob, fixture.UploadId,
            new RowAccess(), BlobRangeIntegrityFixture.PayloadBytes, BlobRangeIntegrityFixture.InitialRevision,
            fixture.BeganAt.AddSeconds(BlobRangeIntegrityFixture.DefaultUploadTtlSeconds), BlobRangeIntegrityFixture.Parts,
            BlobRangeIntegrityFixture.PayloadBytes, fixture.Metadata.IntegrityHash!, BlobUploadStatus.Complete,
            BlobRangeIntegrityFixture.EmptyCount, BlobRangeIntegrityFixture.EmptyCount);
        await Record(database, BlobRangeIntegrityOracle.Key(BlobRangeIntegrityFixture.HeadSpace, fixture), head);
        await Record(database, BlobRangeIntegrityOracle.Key(BlobRangeIntegrityFixture.StateSpace, fixture), state);
        var quota = new BlobQuota(BlobRangeIntegrityFixture.FormatVersion, incarnation, BlobRangeIntegrityFixture.PayloadBytes,
            BlobRangeIntegrityFixture.SingleCount, BlobRangeIntegrityFixture.SingleCount, BlobRangeIntegrityFixture.EmptyCount);
        await Record(database, KeyCodec.Encode(BlobRangeIntegrityFixture.QuotaSpace, fixture.Blob.Partition.TenantId,
            fixture.Blob.Partition.DatabaseId, fixture.Blob.Partition.TransactionDomainId, fixture.Blob.Resource), quota);
        await Record(database, KeyCodec.Encode(BlobRangeIntegrityFixture.GlobalSpace), quota);
        await Part(database, fixture, BlobRangeIntegrityFixture.FirstOrdinal,
            BlobRangeIntegrityFixture.Payload.AsMemory(BlobRangeIntegrityFixture.FirstOrdinal, BlobRangeIntegrityFixture.RawPartBytes));
        await Part(database, fixture, BlobRangeIntegrityFixture.SecondOrdinal,
            BlobRangeIntegrityFixture.Payload.AsMemory(BlobRangeIntegrityFixture.RawPartBytes));
        var operations = new BlobStorageOperations(database);
        await BlobWholeFlowAssertions.Exact(operations.Metadata(BlobRangeIntegrityFixture.Principal, new(fixture.Blob)), fixture.Metadata);
        await BlobWholeFlowAssertions.Exact(operations.UploadInfo(BlobRangeIntegrityFixture.Principal,
            new(fixture.Blob, fixture.UploadId)), state.Info);
    }

    internal static async Task Healthy(DatabaseEngine database, ZoneTreeStore store, BlobRangeIntegrityFixture fixture)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        await NativeState(database, fixture);
        await BlobRangeAdmissionAssertions.RefusedThenHealthy(database, store, fixture);
        await Range(database, fixture, BlobRangeIntegrityFixture.FirstOrdinal, BlobRangeIntegrityFixture.RawPartBytes);
        await Range(database, fixture, BlobRangeIntegrityFixture.RawPartBytes, BlobRangeIntegrityFixture.TailBytes);
        await Range(database, fixture, BlobRangeIntegrityFixture.CrossOffset, BlobRangeIntegrityFixture.RawPartBytes);
        await Range(database, fixture, BlobRangeIntegrityFixture.PayloadBytes - BlobRangeIntegrityFixture.SingleCount,
            BlobRangeIntegrityFixture.SingleCount);
        await Range(database, fixture, BlobRangeIntegrityFixture.PayloadBytes, BlobRangeIntegrityFixture.EmptyCount);
        await Replay(database, fixture);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task Refused(DatabaseEngine database, ZoneTreeStore store,
        BlobRangeIntegrityFixture fixture, BlobRangeFault fault)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        await Range(database, fixture, BlobRangeIntegrityFixture.FirstOrdinal, BlobRangeIntegrityFixture.RawPartBytes);
        var operations = new BlobStorageOperations(database);
        var request = new BlobReadRequest(fixture.Blob, BlobRangeIntegrityFixture.Revision,
            BlobRangeIntegrityFixture.CrossOffset, BlobRangeIntegrityFixture.RawPartBytes);
        var unsupported = fault == BlobRangeFault.UnsupportedPartMetadata;
        var expectedCode = unsupported ? ErrorCode.FormatUnsupported : ErrorCode.Corruption;
        var expectedDetail = unsupported ? BlobRangeIntegrityFixture.UnsupportedDetail : BlobRangeIntegrityFixture.CorruptionDetail;
        var first = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read(BlobRangeIntegrityFixture.Principal, request));
        await Assert.That(first.Code).IsEqualTo(expectedCode);
        await Assert.That(first.Message).IsEqualTo(expectedDetail);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read(BlobRangeIntegrityFixture.Principal, request));
        await Assert.That(repeated.Code).IsEqualTo(expectedCode);
        await Assert.That(repeated.Message).IsEqualTo(expectedDetail);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
        await Range(database, fixture, BlobRangeIntegrityFixture.FirstOrdinal, BlobRangeIntegrityFixture.RawPartBytes);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task Range(DatabaseEngine database, BlobRangeIntegrityFixture fixture, int offset, int count)
    {
        var read = new BlobStorageOperations(database).Read(BlobRangeIntegrityFixture.Principal,
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision, offset, count));
        var expected = new BlobReadResult(fixture.Metadata, offset, BlobRangeIntegrityFixture.Payload.AsMemory(offset, count));
        await BlobWholeFlowAssertions.Exact(read, expected);
    }

    private static async Task Replay(DatabaseEngine database, BlobRangeIntegrityFixture fixture)
    {
        foreach (var command in fixture.Commands)
        {
            var replay = database.ApplyEmbedded(new(command.CommandId, command.Kind,
                BlobRangeIntegrityFixture.Principal, default, command.PayloadJson), cancellationToken: default);
            await BlobWholeFlowAssertions.Exact(replay, command.Original);
        }
    }

    private static async Task Record<T>(DatabaseEngine database, byte[] key, T expected)
    {
        var bytes = database.Store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!.AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    private static async Task Part(DatabaseEngine database, BlobRangeIntegrityFixture fixture, int ordinal, ReadOnlyMemory<byte> expected)
    {
        var key = BlobRangeIntegrityOracle.Key(BlobRangeIntegrityFixture.PartSpace, fixture, ordinal);
        var bytes = database.Store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!.AsSpan().SequenceEqual(expected.Span)).IsTrue();
        await Record(database, BlobRangeIntegrityOracle.Key(BlobRangeIntegrityFixture.PartMetadataSpace, fixture, ordinal),
            new BlobPartMetadata(BlobRangeIntegrityFixture.FormatVersion, expected.Length, BlobRangeIntegrityOracle.Hash(expected.Span)));
    }
}
