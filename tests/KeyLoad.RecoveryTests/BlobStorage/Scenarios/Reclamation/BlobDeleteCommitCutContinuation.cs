using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.RecoveryHost.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobDeleteCommitCutContinuation
{
    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> ReclaimAsync(DatabaseEngine owner,
        BlobCommitCutSeedProof proof, Command<DatabaseCommand> selected)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), BlobCommitCutProtocol.Blob, BlobCommitCutProtocol.OriginalUpload, 1);
        var operation = owner.CreateCommand(OperationKind.ReclaimBlob, request.CommandId, BlobCommitCutProtocol.Principal,
            owner.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var cut = owner.Store.Position;
        var result = owner.Apply(operation);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var value = result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobDeleteCommitCutOracle.ExactAsync(value.Value, new BlobReclaimResult(BlobCommitCutProtocol.OriginalUpload, 1, 0, 4, true));
        await BlobDeleteCommitCutOracle.ExactAsync(value.Receipt, new CommitReceipt(request.CommandId, new CommitToken(proof.Incarnation, BlobCommitCutProtocol.Partition.AtomicPartitionId, cut + 1), [], DurabilityProfile.ProcessDurable, 1));
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
            var operation = NativeSerialization.Deserialize<Command<DatabaseCommand>>(proof.Operations[index]);
            if (operation.Value!.Kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal)
            {
                var original = NativeSerialization.Deserialize<Result<object>>(proof.Results[index]);
                await ReplayAsync(owner, operation, original);
                continue;
            }
            var result = owner.ResolveOutcome(operation);
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(Errors.Code(owner.Apply(operation).Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        }
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, Command<DatabaseCommand> operation, Result<object> result)
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
