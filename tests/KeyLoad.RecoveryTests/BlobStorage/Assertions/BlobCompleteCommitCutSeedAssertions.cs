using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCompleteCommitCutSeedAssertions
{
    internal static async Task AssertAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof)
    {
        for (var index = 0; index < proof.Operations.Length; index++)
        {
            var operation = NativeSerialization.Deserialize<Command<DatabaseCommand>>(proof.Operations[index]);
            var result = NativeSerialization.Deserialize<Result<object>>(proof.Results[index]);
            await Assert.That(Errors.Code(result.Problem)).IsNull();
            if (operation.Value!.Kind == OperationKind.ConfigurePrincipal)
            {
                await BlobCompleteCommitCutOracle.ExactAsync(result.Get<PrincipalRecord>(),
                    new PrincipalRecord(BlobCommitCutProtocol.DeniedPrincipal, BlobCommitCutProtocol.Partition.TenantId, [], []));
                continue;
            }
            if (operation.Value!.Kind == OperationKind.ConfigureResource)
            {
                continue;
            }
            var receipt = await ValueAsync(proof, operation, result);
            await BlobCompleteCommitCutOracle.ExactAsync(receipt, new CommitReceipt(operation.CommandId, new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId,
                    proof.Position - proof.Operations.Length + index + 1), [], DurabilityProfile.ProcessDurable, 1));
            await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
            await Assert.That(receipt.Token.Incarnation).IsEqualTo(proof.Incarnation);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(BlobCommitCutProtocol.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsEqualTo(proof.Position - proof.Operations.Length + index + 1);
            await Assert.That(receipt.Durability).IsEqualTo(owner.Store.Identity.Durability);
            await Assert.That(receipt.Mutations).IsEmpty();
        }
    }

    private static async Task<CommitReceipt> ValueAsync(BlobCommitCutSeedProof proof, Command<DatabaseCommand> operation, Result<object> result)
    {
        if (operation.Value!.Kind == OperationKind.CompleteBlobUpload)
        {
            var completed = result.Get<BlobCommitResult<BlobMetadata>>();
            await BlobCompleteCommitCutOracle.ExactAsync(completed.Value, BlobCompleteCommitCutOracle.Metadata(proof,
                operation, BlobCommitCutProtocol.OriginalUpload, BlobCompleteCommitCutOracle.OriginalBytes(), 1));
            return completed.Receipt;
        }
        var upload = operation.Value!.Kind == OperationKind.BeginBlobUpload
            ? NativeSerialization.Deserialize<BeginBlobUploadRequest>((operation).Value!.Input.Span).UploadId
            : NativeSerialization.Deserialize<WriteBlobPartRequest>((operation).Value!.Input.Span).UploadId;
        var begin = BlobCompleteCommitCutOracle.Begin(proof, upload);
        var bytes = upload == BlobCommitCutProtocol.OriginalUpload
            ? BlobCompleteCommitCutOracle.OriginalBytes() : BlobCompleteCommitCutOracle.MultipartPayload();
        var ordinal = operation.Value!.Kind == OperationKind.BeginBlobUpload
            ? -1 : NativeSerialization.Deserialize<WriteBlobPartRequest>((operation).Value!.Input.Span).Ordinal;
        var length = ordinal < 0 ? 0 : Math.Min(bytes.Length, (ordinal + 1) * BlobCompleteCommitCutOracle.ChunkBytes);
        var hash = BlobCompleteCommitCutOracle.Initial(proof.Incarnation, BlobCommitCutProtocol.Blob, upload, bytes.Length);
        for (var part = 0; part <= ordinal; part++)
        {
            var offset = part * BlobCompleteCommitCutOracle.ChunkBytes;
            hash = BlobCompleteCommitCutOracle.Next(hash, part,
                bytes.AsSpan(offset, Math.Min(BlobCompleteCommitCutOracle.ChunkBytes, bytes.Length - offset)));
        }
        var info = result.Get<BlobCommitResult<BlobUploadInfo>>();
        await BlobCompleteCommitCutOracle.ExactAsync(info.Value, new BlobUploadInfo(BlobCommitCutProtocol.Blob, upload,
            bytes.Length, upload == BlobCommitCutProtocol.OriginalUpload ? 0 : 1, ordinal + 1, length,
            begin.EvaluatedAt.AddSeconds(BlobCompleteCommitCutOracle.UploadLifetimeSeconds), BlobUploadStatus.Active, hash));
        return info.Receipt;
    }
}
