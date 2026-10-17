using KeyLoad.Core;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobDeleteCommitCutContinuation
{
    internal static async Task<(ReplicatedOperation Operation, OperationResult Result)> ReclaimAsync(DatabaseEngine owner,
        BlobCommitCutSeedProof proof, ReplicatedOperation selected)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), BlobCommitCutProtocol.Blob, BlobCommitCutProtocol.OriginalUpload, 1);
        var operation = owner.CreateNativeOperation(OperationKind.ReclaimBlob, request.CommandId, BlobCommitCutProtocol.Principal,
            owner.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var cut = owner.Store.Position;
        var result = owner.Apply(operation);
        await Assert.That(result.Error).IsNull();
        var value = result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobDeleteCommitCutOracle.ExactAsync(value.Value, new BlobReclaimResult(BlobCommitCutProtocol.OriginalUpload, 1, 0, 4, true));
        await BlobDeleteCommitCutOracle.ExactAsync(value.Receipt, new CommitReceipt(request.CommandId,
            new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId, cut + 1, 1), [], DurabilityProfile.ProcessDurable));
        await Assert.That(value.Receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(value.Receipt.Token.Position).IsEqualTo(cut + 1);
        await Assert.That(owner.Store.Position).IsEqualTo(cut + 1);
        await BlobDeleteCommitCutAssertions.PublishedAsync(owner, proof, selected, deleted: true, reclaimed: true);
        await ReplayAsync(owner, operation, result);
        return (operation, result);
    }

    internal static async Task InvalidatedSeedsAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var position = store.Position;
        for (var index = 0; index < proof.Operations.Length; index++)
        {
            var operation = NativeSerialization.Deserialize<ReplicatedOperation>(proof.Operations[index]);
            if (operation.Kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal)
            {
                var original = NativeSerialization.Deserialize<OperationResult>(proof.Results[index]);
                await ReplayAsync(owner, operation, original);
                continue;
            }
            var result = owner.ResolveOutcome(operation);
            await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(owner.Apply(operation).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        }
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ReplicatedOperation operation, OperationResult result)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var cut = store.Position;
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Apply(operation), result);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.ResolveOutcome(operation), result);
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }
}
