using ManagedCode.Communication;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobReclaimWholeFlow
{
    internal static async Task<BlobReclaimWholeFlowFixture> Start(TestDatabase database)
    {
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobReclaimWholeFlowFixture.BlobId);
        var upload = Guid.NewGuid();
        var begin = BlobStorageTestSupport.Begin(database, blob, upload,
            BlobReclaimWholeFlowFixture.DeclaredBytes, BlobReclaimWholeFlowFixture.InitialRevision);
        await BlobWholeFlowAssertions.Quotas(database.Database, blob, BlobReclaimWholeFlowFixture.DeclaredBytes,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.SingleCount);
        var first = BlobStorageTestSupport.Write(database, blob, upload, BlobReclaimWholeFlowFixture.FirstOrdinal,
            BlobReclaimWholeFlowFixture.Payload.AsMemory(BlobReclaimWholeFlowFixture.FirstOrdinal, BlobLimits.RawPartBytes));
        var second = BlobStorageTestSupport.Write(database, blob, upload, BlobReclaimWholeFlowFixture.SecondOrdinal,
            BlobReclaimWholeFlowFixture.Payload.AsMemory(BlobLimits.RawPartBytes, BlobLimits.RawPartBytes));
        var chain = BlobIntegrity.NextHash(begin.Value.IntegrityHash, BlobReclaimWholeFlowFixture.FirstOrdinal,
            BlobLimits.RawPartBytes, BlobIntegrity.PartHash(BlobReclaimWholeFlowFixture.Payload.AsSpan(BlobReclaimWholeFlowFixture.FirstOrdinal, BlobLimits.RawPartBytes)));
        await Assert.That(first.Value.IntegrityHash).IsEqualTo(chain);
        chain = BlobIntegrity.NextHash(chain, BlobReclaimWholeFlowFixture.SecondOrdinal,
            BlobLimits.RawPartBytes, BlobIntegrity.PartHash(BlobReclaimWholeFlowFixture.Payload.AsSpan(BlobLimits.RawPartBytes)));
        await BlobWholeFlowAssertions.Exact(second.Value, begin.Value with
        {
            NextOrdinal = BlobReclaimWholeFlowFixture.AcceptedParts,
            StoredBytes = BlobReclaimWholeFlowFixture.StoredBytes,
            IntegrityHash = chain
        });
        var abort = new AbortBlobUploadRequest(Guid.NewGuid(), blob, upload);
        var result = Apply(database.Database, OperationKind.AbortBlobUpload, abort, abort.CommandId);
        var aborted = result.Get<BlobCommitResult<BlobUploadInfo>>();
        await BlobWholeFlowAssertions.Exact(aborted.Value, second.Value with
        {
            Status = BlobUploadStatus.Aborted
        });
        await BlobWholeFlowAssertions.Receipt(database.Database, aborted.Receipt, abort.CommandId, blob);
        await BlobWholeFlowAssertions.Quotas(database.Database, blob, BlobReclaimWholeFlowFixture.StoredBytes,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
        var fixture = new BlobReclaimWholeFlowFixture(blob, upload, abort, aborted.Value);
        await BlobWholeFlowAssertions.Parts(database.Database, fixture, true, true);
        await Assert.That(new BlobStorageOperations(database.Database).Metadata(BlobReclaimWholeFlowFixture.Principal, new(blob))).IsNull();
        await Replay(database.Database, database.Store, OperationKind.AbortBlobUpload, abort, abort.CommandId, result);
        return fixture;
    }

    internal static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
        => database.ApplyEmbedded(database.CreateCommand(kind, id, BlobReclaimWholeFlowFixture.Principal, default, JsonSerializer.Serialize(payload, JsonDefaults.Options)), cancellationToken: default);

    internal static async Task Replay<T>(DatabaseEngine database, ZoneTreeStore store,
        OperationKind kind, T request, Guid id, Result<object> original)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        await BlobWholeFlowAssertions.Exact(Apply(database, kind, request, id), original);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task<(ReclaimBlobRequest Request, Result<object> Result)> First(
        DatabaseEngine database, ZoneTreeStore store, BlobReclaimWholeFlowFixture fixture)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId, BlobReclaimWholeFlowFixture.SingleCount);
        var result = Apply(database, OperationKind.ReclaimBlob, request, request.CommandId);
        var reclaimed = result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobWholeFlowAssertions.Exact(reclaimed.Value, new BlobReclaimResult(fixture.UploadId,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.SingleCount, BlobLimits.RawPartBytes, false));
        await BlobWholeFlowAssertions.Receipt(database, reclaimed.Receipt, request.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(database, fixture.Blob, BlobLimits.RawPartBytes,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Parts(database, fixture, false, true);
        await AssertState(database, fixture);
        await Replay(database, store, OperationKind.ReclaimBlob, request, request.CommandId, result);
        return (request, result);
    }

    internal static async Task AssertState(DatabaseEngine database, BlobReclaimWholeFlowFixture fixture)
    {
        var state = database.Store.Read(view => BlobRecordReader.Get<BlobState>(view, BlobKeys.State(fixture.Blob, fixture.UploadId)))!;
        await Assert.That(state.ReclaimCursor).IsEqualTo(BlobReclaimWholeFlowFixture.SecondOrdinal);
        await Assert.That(state.RemainingReservation).IsEqualTo(BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Exact(state.Info, fixture.Aborted with
        {
            StoredBytes = BlobLimits.RawPartBytes
        });
        await BlobWholeFlowAssertions.Exact(new BlobStorageOperations(database).UploadInfo(BlobReclaimWholeFlowFixture.Principal,
            new(fixture.Blob, fixture.UploadId)), state.Info);
    }

    internal static async Task<ReclaimBlobRequest> Finish(DatabaseEngine database, ZoneTreeStore store, BlobReclaimWholeFlowFixture fixture)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId, BlobReclaimWholeFlowFixture.SingleCount);
        var result = Apply(database, OperationKind.ReclaimBlob, request, request.CommandId);
        var final = result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobWholeFlowAssertions.Exact(final.Value, new BlobReclaimResult(fixture.UploadId,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount, BlobLimits.RawPartBytes, true));
        await BlobWholeFlowAssertions.Receipt(database, final.Receipt, request.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(database, fixture.Blob, BlobReclaimWholeFlowFixture.EmptyCount,
            BlobReclaimWholeFlowFixture.EmptyCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Parts(database, fixture, false, false);
        await Assert.That(new BlobStorageOperations(database).UploadInfo(BlobReclaimWholeFlowFixture.Principal,
            new(fixture.Blob, fixture.UploadId))).IsNull();
        await Replay(database, store, OperationKind.ReclaimBlob, request, request.CommandId, result);
        return request;
    }
}
