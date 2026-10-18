using ManagedCode.Communication.Commands;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedLifetimeOperations
{
    internal static BlobRangeRetainedOperation Send<T>(DatabaseEngine owner, OperationKind kind, Guid id, T payload, DateTimeOffset? now = null)
    {
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var result = owner.Apply(owner.CreateCommand(kind, id, "root", now ?? owner.EvaluationClock.GetUtcNow(), json));
        return new(kind, id, json, result);
    }

    internal static Command<DatabaseCommand> Original(BlobRangeRetainedOperation command)
        => new(command.CommandId, command.Kind, "root", default, command.PayloadJson);

    internal static BlobPublishedOwnedState Damage(ZoneTreeStore store, BlobRangeIntegrityFixture fixture, bool missing)
    {
        var key = BlobRangeIntegrityOracle.Key("blob-state-v1", fixture);
        var bytes = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The published fixture must own its state row.");
        var state = NativeSerialization.Deserialize<BlobState>(bytes);
        store.Commit((transaction, _) =>
        {
            if (missing)
            {
                transaction.Delete(key);
            }
            else
            {
                transaction.PutRecord(key, state with
                {
                    Retired = true
                });
            }
            return true;
        });
        return new(key, bytes);
    }

    internal static void Repair(ZoneTreeStore store, BlobPublishedOwnedState fault)
        => store.Commit((transaction, _) => { transaction.Put(fault.Key, fault.Original); return true; });

    internal static async Task<BlobPublishedEmptyLifetime> PublishEmpty(DatabaseEngine owner, BlobRangeIntegrityFixture fixture)
    {
        var blob = fixture.Blob;
        var upload = fixture.UploadId;
        var request = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 0, 2);
        var now = owner.EvaluationClock.GetUtcNow();
        var hash = Convert.ToHexStringLower(SHA256.HashData(KeyCodec.Encode("blob-chain-v1",
            owner.Store.Identity.Incarnation.ToString("N"), blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
            upload.ToString("N"), 0L, 65_536L)));
        var json = JsonSerializer.Serialize(request, JsonDefaults.Options);
        var begun = new BlobRangeRetainedOperation(OperationKind.BeginBlobUpload, request.CommandId, json,
            owner.Apply(owner.CreateCommand(OperationKind.BeginBlobUpload, request.CommandId, "root", now, json)));
        var expected = new BlobUploadInfo(blob, upload, 0, 2, 0, 0, now.AddSeconds(3_600), BlobUploadStatus.Active, hash);
        await BlobWholeFlowAssertions.Exact(begun.Original.Get<BlobCommitResult<BlobUploadInfo>>().Value, expected);
        await BlobWholeFlowAssertions.Receipt(owner, begun.Original.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, request.CommandId, blob);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completedAt = owner.EvaluationClock.GetUtcNow();
        var completedJson = JsonSerializer.Serialize(complete, JsonDefaults.Options);
        var completed = new BlobRangeRetainedOperation(OperationKind.CompleteBlobUpload, complete.CommandId, completedJson,
            owner.Apply(owner.CreateCommand(OperationKind.CompleteBlobUpload, complete.CommandId, "root", completedAt, completedJson)));
        var metadata = new BlobMetadata(blob, 3, upload, 0, 0, hash, new RowAccess(), completedAt);
        await BlobWholeFlowAssertions.Exact(completed.Original.Get<BlobCommitResult<BlobMetadata>>().Value, metadata);
        await BlobWholeFlowAssertions.Receipt(owner, completed.Original.Get<BlobCommitResult<BlobMetadata>>().Receipt, complete.CommandId, blob);
        return new(metadata, expected, begun, completed);
    }
}
