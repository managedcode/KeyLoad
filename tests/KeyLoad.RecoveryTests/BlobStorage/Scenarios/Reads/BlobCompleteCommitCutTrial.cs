using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCompleteCommitCutTrial
{
    private const string MissingOutcome = "The applied command has no durable outcome.";
    private const int CommitDelta = 1;

    internal static async Task RunAsync(string root, BlobCommitCutKind cut, CancellationToken token)
    {
        var proof = await BlobCommitCutSeedProofReader.LoadAsync(root, token);
        var original = await BlobCommitCutNativeProof.OriginalOperationAsync(root, token);
        await BlobCompleteCommitCutOracle.RequireSelectedAsync(proof, original);
        if (cut == BlobCommitCutKind.UnknownAfterJournalFlush)
        {
            await BlobCommitCutNativeProof.AssertFaultAsync(root, token);
        }
        using var recovered = BlobCommitCutNativeProof.Open(root);
        var owner = BlobCommitCutNativeProof.Database(recovered);
        var committed = cut != BlobCommitCutKind.HeaderWritten;
        await Assert.That(recovered.Position).IsEqualTo(proof.Position + (committed ? CommitDelta : 0));
        if (!committed)
        {
            await Assert.That(BlobCommitCutNativeProof.Image(recovered)).IsEqualTo(proof.RawImage);
        }
        await BlobCompleteCommitCutAssertions.PublishedAsync(owner, proof, original, committed);
        await BlobCompleteCommitCutSeedAssertions.AssertAsync(owner, proof);
        await BlobCommitCutNativeProof.ReplaySeedsAsync(owner, proof);
        await BlobCompleteCommitCutAssertions.DeniedAsync(owner, original);
        await BlobCommitCutNativeProof.DeniedOutcomesAsync(owner, proof, original);
        var receipt = await CompleteOriginalAsync(owner, proof, original, cut, root, token);
        await BlobCompleteCommitCutAssertions.PublishedAsync(owner, proof, original, committed: true);
        await BlobCommitCutNativeProof.ReplaySeedsAsync(owner, proof);
        var firstImage = BlobCommitCutNativeProof.Image(recovered);
        var firstCut = recovered.Position;
        recovered.Dispose();
        using var cold = BlobCommitCutNativeProof.Open(root);
        var second = BlobCommitCutNativeProof.Database(cold);
        await Assert.That(BlobCommitCutNativeProof.Image(cold)).IsEqualTo(firstImage);
        await Assert.That(cold.Position).IsEqualTo(firstCut);
        await ReplayAsync(second, original, receipt);
        await BlobCompleteCommitCutAssertions.PublishedAsync(second, proof, original, committed: true);
        var healthy = await BlobCompleteCommitCutHealthy.RunAsync(second, token);
        await BlobCompleteCommitCutAssertions.PublishedAsync(second, proof, original, committed: true, healthy: true);
        var image = BlobCommitCutNativeProof.Image(cold);
        var position = cold.Position;
        cold.Dispose();
        using var finalStore = BlobCommitCutNativeProof.Open(root);
        var final = BlobCommitCutNativeProof.Database(finalStore);
        await Assert.That(BlobCommitCutNativeProof.Image(finalStore)).IsEqualTo(image);
        await Assert.That(finalStore.Position).IsEqualTo(position);
        await BlobCompleteCommitCutAssertions.PublishedAsync(final, proof, original, committed: true, healthy: true);
        await BlobCommitCutNativeProof.ReplaySeedsAsync(final, proof);
        await ReplayAsync(final, original, receipt);
        await BlobCompleteCommitCutHealthy.ReplayAsync(final, healthy);
        await BlobCompleteCommitCutAssertions.DeniedAsync(final, original);
        await BlobCommitCutNativeProof.DeniedOutcomesAsync(final, proof, original);
        await Assert.That(BlobCommitCutNativeProof.Image(finalStore)).IsEqualTo(image);
        await Assert.That(finalStore.Position).IsEqualTo(position);
    }

    private static async Task<Result<object>> CompleteOriginalAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof,
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
                await BlobCompleteCommitCutOracle.ExactAsync(resolved, await BlobCommitCutNativeProof.ReturnedAsync(root, token));
            }
        }
        var result = owner.Apply(original);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var completed = result.Get<BlobCommitResult<BlobMetadata>>();
        await BlobCompleteCommitCutOracle.ExactAsync(completed.Value, BlobCompleteCommitCutOracle.Metadata(proof,
            original, BlobCommitCutProtocol.NewUpload, BlobCompleteCommitCutOracle.MultipartPayload(), 2));
        var receipt = completed.Receipt;
        await BlobCompleteCommitCutOracle.ExactAsync(receipt, new CommitReceipt(original.CommandId, new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId,
                proof.Position + CommitDelta), [], DurabilityProfile.ProcessDurable, 1));
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(proof.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(BlobCommitCutProtocol.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(proof.Position + CommitDelta);
        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
        await Assert.That(store.Position).IsEqualTo(proof.Position + CommitDelta);
        if (cut != BlobCommitCutKind.HeaderWritten)
        {
            await BlobCompleteCommitCutOracle.ExactAsync(result, resolved);
            await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        }
        await ReplayAsync(owner, original, result);
        return result;
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, Command<DatabaseCommand> original, Result<object> result)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var cut = store.Position;
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Apply(original), result);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.ResolveOutcome(original), result);
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }
}
