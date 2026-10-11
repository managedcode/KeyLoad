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
            var operation = NativeSerialization.Deserialize<ReplicatedOperation>(proof.Operations[index]);
            var result = NativeSerialization.Deserialize<OperationResult>(proof.Results[index]);
            await Assert.That(result.Error).IsNull();
            if (operation.Kind == OperationKind.ConfigurePrincipal)
            {
                await BlobCompleteCommitCutOracle.ExactAsync(result.Get<PrincipalRecord>(),
                    new PrincipalRecord(BlobCommitCutProtocol.DeniedPrincipal, BlobCommitCutProtocol.Partition.TenantId, [], []));
                continue;
            }
            if (operation.Kind == OperationKind.ConfigureResource)
            { continue; }
            var receipt = await ValueAsync(proof, operation, result);
            await BlobCompleteCommitCutOracle.ExactAsync(receipt, new CommitReceipt(operation.Id,
                new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId,
                    proof.Position - proof.Operations.Length + index + 1, 1), [], DurabilityProfile.ProcessDurable));
            await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
            await Assert.That(receipt.Token.Incarnation).IsEqualTo(proof.Incarnation);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(BlobCommitCutProtocol.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsEqualTo(proof.Position - proof.Operations.Length + index + 1);
            await Assert.That(receipt.Durability).IsEqualTo(owner.Store.Identity.Durability);
            await Assert.That(receipt.Mutations).IsEmpty();
        }
    }

    private static async Task<CommitReceipt> ValueAsync(BlobCommitCutSeedProof proof, ReplicatedOperation operation, OperationResult result)
    {
        if (operation.Kind == OperationKind.CompleteBlobUpload)
        {
            var completed = result.Get<BlobCommitResult<BlobMetadata>>();
            await BlobCompleteCommitCutOracle.ExactAsync(completed.Value, BlobCompleteCommitCutOracle.Metadata(proof,
                operation, BlobCommitCutProtocol.OriginalUpload, BlobCompleteCommitCutOracle.OriginalBytes(), 1));
            return completed.Receipt;
        }
        var upload = operation.Kind == OperationKind.BeginBlobUpload
            ? NativeCommandPayload.Read<BeginBlobUploadRequest>(operation).UploadId
            : NativeCommandPayload.Read<WriteBlobPartRequest>(operation).UploadId;
        var begin = BlobCompleteCommitCutOracle.Begin(proof, upload);
        var bytes = upload == BlobCommitCutProtocol.OriginalUpload
            ? BlobCompleteCommitCutOracle.OriginalBytes() : BlobCompleteCommitCutOracle.MultipartPayload();
        var ordinal = operation.Kind == OperationKind.BeginBlobUpload
            ? -1 : NativeCommandPayload.Read<WriteBlobPartRequest>(operation).Ordinal;
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
