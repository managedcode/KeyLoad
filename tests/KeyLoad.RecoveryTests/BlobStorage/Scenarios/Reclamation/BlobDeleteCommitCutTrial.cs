using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobDeleteCommitCutTrial
{
    private const string MissingOutcome = "The applied command has no durable outcome.";

    internal static async Task RunAsync(string root, BlobCommitCutKind cut, CancellationToken token)
    {
        var proof = await BlobCommitCutSeedProofReader.LoadAsync(root, token);
        var original = await BlobCommitCutNativeProof.OriginalOperationAsync(root, token);
        await BlobDeleteCommitCutOracle.RequireSelectedAsync(original);
        if (cut == BlobCommitCutKind.UnknownAfterJournalFlush)
        {
            await BlobCommitCutNativeProof.AssertFaultAsync(root, token);
        }
        using var recovered = BlobCommitCutNativeProof.Open(root);
        var owner = BlobCommitCutNativeProof.Database(recovered);
        var committed = cut != BlobCommitCutKind.HeaderWritten;
        await Assert.That(recovered.Position).IsEqualTo(proof.Position + (committed ? 1 : 0));
        if (!committed)
        {
            await Assert.That(BlobCommitCutNativeProof.Image(recovered)).IsEqualTo(proof.RawImage);
        }
        await BlobDeleteCommitCutAssertions.PublishedAsync(owner, proof, original, committed);
        await BlobCompleteCommitCutSeedAssertions.AssertAsync(owner, proof);
        await BlobCommitCutNativeProof.ReplaySeedsAsync(owner, proof);
        await BlobDeleteCommitCutAssertions.DeniedAsync(owner);
        await BlobCommitCutNativeProof.DeniedOutcomesAsync(owner, proof, original);
        var result = await OriginalAsync(owner, proof, original, cut, root, token);
        await BlobDeleteCommitCutAssertions.PublishedAsync(owner, proof, original, deleted: true);
        await BlobCommitCutNativeProof.ReplaySeedsAsync(owner, proof);
        var image = BlobCommitCutNativeProof.Image(recovered);
        var position = recovered.Position;
        recovered.Dispose();
        using var cold = BlobCommitCutNativeProof.Open(root);
        var second = BlobCommitCutNativeProof.Database(cold);
        await Assert.That(BlobCommitCutNativeProof.Image(cold)).IsEqualTo(image);
        await Assert.That(cold.Position).IsEqualTo(position);
        await BlobDeleteCommitCutAssertions.PublishedAsync(second, proof, original, deleted: true);
        await BlobDeleteCommitCutContinuation.ReplayAsync(second, original, result);
        var reclaim = await BlobDeleteCommitCutContinuation.ReclaimAsync(second, proof, original);
        var healthy = await BlobCompleteCommitCutHealthy.RunAsync(second, token);
        await BlobDeleteCommitCutAssertions.QuotasAsync(second, 4, 1, objects: 2);
        image = BlobCommitCutNativeProof.Image(cold);
        position = cold.Position;
        cold.Dispose();
        using var finalStore = BlobCommitCutNativeProof.Open(root);
        var final = BlobCommitCutNativeProof.Database(finalStore);
        await Assert.That(BlobCommitCutNativeProof.Image(finalStore)).IsEqualTo(image);
        await Assert.That(finalStore.Position).IsEqualTo(position);
        await BlobCommitCutNativeProof.AssertAuthorityAsync(finalStore, final, proof);
        await BlobDeleteCommitCutContinuation.ReplayAsync(final, original, result);
        await BlobDeleteCommitCutContinuation.ReplayAsync(final, reclaim.Operation, reclaim.Result);
        await BlobDeleteCommitCutContinuation.InvalidatedSeedsAsync(final, proof);
        await BlobCompleteCommitCutHealthy.ReplayAsync(final, healthy);
        await BlobDeleteCommitCutAssertions.RetainedAfterHealthyAsync(final, proof, original);
        await BlobDeleteCommitCutAssertions.QuotasAsync(final, 4, 1, objects: 2);
        await BlobDeleteCommitCutAssertions.DeniedAsync(final);
        await BlobCommitCutNativeProof.DeniedOutcomesAsync(final, proof, original);
        await Assert.That(BlobCommitCutNativeProof.Image(finalStore)).IsEqualTo(image);
        await Assert.That(finalStore.Position).IsEqualTo(position);
    }

    private static async Task<Result<object>> OriginalAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof,
        Command<DatabaseCommand> original, BlobCommitCutKind cut, string root, CancellationToken token)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var position = store.Position;
        var resolved = owner.ResolveOutcome(original);
        if (cut == BlobCommitCutKind.HeaderWritten)
        {
            await Assert.That(Errors.Code(resolved.Problem)).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(resolved.Problem?.Detail).IsEqualTo(MissingOutcome);
            
            await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
            await Assert.That(store.Position).IsEqualTo(position);
        }
        else
        {
            await Assert.That(Errors.Code(resolved.Problem)).IsNull();
            if (cut == BlobCommitCutKind.Acknowledged)
            {
                await BlobDeleteCommitCutOracle.ExactAsync(resolved, await BlobCommitCutNativeProof.ReturnedAsync(root, token));
            }
        }
        var result = owner.Apply(original);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var receipt = result.Get<BlobCommitResult<BlobMetadata>>().Receipt;
        await BlobDeleteCommitCutOracle.ExactAsync(receipt, new CommitReceipt(original.CommandId, new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId, proof.Position + 1), [], DurabilityProfile.ProcessDurable, 1));
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(proof.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(BlobCommitCutProtocol.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(proof.Position + 1);
        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
        await BlobDeleteCommitCutOracle.ExactAsync(result.Get<BlobCommitResult<BlobMetadata>>().Value,
            new BlobMetadata(BlobCommitCutProtocol.Blob, 2, null, 0, 0, null, new RowAccess(), new DateTimeOffset(original.Timestamp), true));
        await Assert.That(store.Position).IsEqualTo(proof.Position + 1);
        if (cut != BlobCommitCutKind.HeaderWritten)
        {
            await BlobDeleteCommitCutOracle.ExactAsync(result, resolved);
            await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        }
        await BlobDeleteCommitCutContinuation.ReplayAsync(owner, original, result);
        return result;
    }
}
