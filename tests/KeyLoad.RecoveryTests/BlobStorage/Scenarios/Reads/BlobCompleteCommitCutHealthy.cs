using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.RecoveryHost.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCompleteCommitCutHealthy
{
    private const string HealthyId = "healthy-after-complete";
    private const string GuidFormat = "N";
    private const string StateSpace = "blob-state-v1";
    private const string HeadSpace = "blob-head-v1";
    private const string PartSpace = "blob-part-v1";
    private const string PartMetaSpace = "blob-partmeta-v1";
    private const int PayloadBytes = 4;

    internal static async Task<BlobCompleteCommitCutHealthyState> RunAsync(DatabaseEngine owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var blob = BlobCommitCutProtocol.Blob with
        {
            Id = HealthyId
        };
        var upload = Guid.NewGuid();
        byte[] bytes = [101, 102, 103, 104];
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, PayloadBytes, 0);
        var first = await SendAsync(owner, OperationKind.BeginBlobUpload, begin.CommandId, begin);
        var expiry = new DateTimeOffset(first.Operation.Timestamp).AddSeconds(BlobCompleteCommitCutOracle.UploadLifetimeSeconds);
        var initial = BlobCompleteCommitCutOracle.Initial(owner.Store.Identity.Incarnation, blob, upload, PayloadBytes);
        await BlobCompleteCommitCutOracle.ExactAsync(first.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value,
            new BlobUploadInfo(blob, upload, PayloadBytes, 0, 0, 0, expiry, BlobUploadStatus.Active, initial));
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, bytes, BlobCompleteCommitCutOracle.Hash(bytes));
        var second = await SendAsync(owner, OperationKind.WriteBlobPart, part.CommandId, part);
        var hash = BlobCompleteCommitCutOracle.Chain(owner.Store.Identity.Incarnation, blob, upload, bytes);
        await BlobCompleteCommitCutOracle.ExactAsync(second.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value,
            new BlobUploadInfo(blob, upload, PayloadBytes, 0, 1, PayloadBytes, expiry, BlobUploadStatus.Active, hash));
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var third = await SendAsync(owner, OperationKind.CompleteBlobUpload, complete.CommandId, complete);
        var metadata = new BlobMetadata(blob, 1, upload, PayloadBytes, 1, hash, new RowAccess(), new DateTimeOffset(third.Operation.Timestamp));
        await BlobCompleteCommitCutOracle.ExactAsync(third.Result.Get<BlobCommitResult<BlobMetadata>>().Value, metadata);
        var incarnation = owner.Store.Identity.Incarnation;
        var state = new BlobState(1, incarnation, incarnation, begin.CommandId, BlobCommitCutProtocol.Principal,
            blob, upload, new RowAccess(), PayloadBytes, 0, expiry, 1, PayloadBytes, hash, BlobUploadStatus.Complete, 0, 0);
        var result = new BlobCompleteCommitCutHealthyState(blob, upload, bytes, metadata, state,
            [first.Operation, second.Operation, third.Operation], [first.Result, second.Result, third.Result]);
        await ReplayAsync(owner, result);
        return result;
    }

    private static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> SendAsync<T>(DatabaseEngine owner,
        OperationKind kind, Guid id, T command)
    {
        var operation = owner.CreateCommand(kind, id, BlobCommitCutProtocol.Principal,
            owner.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var position = owner.Store.Position;
        var result = owner.Apply(operation);
        if (Errors.Code(result.Problem) is not null)
        {
            throw Errors.Fail(Errors.Code(result.Problem).Value, result.Problem?.Detail!);
        }
        var receipt = kind == OperationKind.CompleteBlobUpload
            ? result.Get<BlobCommitResult<BlobMetadata>>().Receipt : result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt;
        await BlobCompleteCommitCutOracle.ExactAsync(receipt, new CommitReceipt(id, new CommitToken(owner.Store.Identity.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId,
                position + 1), [], DurabilityProfile.ProcessDurable, 1));
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(BlobCommitCutProtocol.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(position + 1);
        await Assert.That(receipt.Durability).IsEqualTo(owner.Store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
        return (operation, result);
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, BlobCompleteCommitCutHealthyState seed)
    {
        var blob = seed.Blob;
        var operations = new BlobStorageOperations(owner);
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Metadata(BlobCommitCutProtocol.Principal, new(blob)), seed.Metadata);
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal, new(blob, 1, 0, PayloadBytes)),
            new BlobReadResult(seed.Metadata, 0, seed.Bytes));
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal, new(blob, 1, 1, 2)),
            new BlobReadResult(seed.Metadata, 1, seed.Bytes.AsMemory(1, 2)));
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal, new(blob, 1, PayloadBytes, 0)),
            new BlobReadResult(seed.Metadata, PayloadBytes, ReadOnlyMemory<byte>.Empty));
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(Key(StateSpace, seed))), seed.State);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(Key(HeadSpace, seed))),
            new BlobHead(1, owner.Store.Identity.Incarnation, seed.Metadata));
        var raw = KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id, seed.Upload.ToString(GuidFormat), 0L);
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(raw))!.AsSpan().SequenceEqual(seed.Bytes)).IsTrue();
        var part = KeySpace.Partition(PartMetaSpace, blob.Partition, blob.Resource, blob.Id, seed.Upload.ToString(GuidFormat), 0L);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(part)),
            new BlobPartMetadata(1, PayloadBytes, BlobCompleteCommitCutOracle.Hash(seed.Bytes)));
        for (var index = 0; index < seed.Operations.Length; index++)
        {
            await BlobCompleteCommitCutTrial.ReplayAsync(owner, seed.Operations[index], seed.Results[index]);
        }
    }

    private static byte[] Key(string space, BlobCompleteCommitCutHealthyState seed)
        => space == StateSpace
            ? KeySpace.Partition(space, seed.Blob.Partition, seed.Blob.Resource, seed.Blob.Id, seed.Upload.ToString(GuidFormat))
            : KeySpace.Partition(space, seed.Blob.Partition, seed.Blob.Resource, seed.Blob.Id);
}
