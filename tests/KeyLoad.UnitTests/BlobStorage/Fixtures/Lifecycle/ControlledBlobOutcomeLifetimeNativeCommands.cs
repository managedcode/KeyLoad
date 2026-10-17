using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeCommands
{
    private const string Prefix = "outcome-lifetime-";
    private const int Length = 4;
    private const int Zero = 0;
    private const int One = 1;
    private const int DeletedRevision = 2;
    internal static readonly OperationKind[] Kinds = [OperationKind.BeginBlobUpload, OperationKind.WriteBlobPart,
        OperationKind.CompleteBlobUpload, OperationKind.AbortBlobUpload, OperationKind.DeleteBlob, OperationKind.ReclaimBlob];

    internal static async Task<ImmutableArray<ControlledBlobOutcomeLifetimeNativeProof>> SeedAsync(
        ControlledBlobOutcomeLifetimeNativeExecution execution, CancellationToken token)
    {
        var selected = ImmutableArray.CreateBuilder<ControlledBlobOutcomeLifetimeNativeProof>();
        foreach (var kind in Kinds)
        {
            selected.Add(await OneAsync(execution, kind, token));
        }
        return selected.ToImmutable();
    }

    internal static async Task<ControlledBlobOutcomeLifetimeNativeProof> RepairedBeginAsync(
        ControlledBlobOutcomeLifetimeNativeExecution execution, CancellationToken token)
    {
        var blob = new BlobRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementBlobSeed.Resource, Prefix + "repaired-" + Guid.NewGuid().ToString("N"));
        var upload = Guid.NewGuid();
        var initial = BlobIntegrity.InitialHash(execution.Target.Store.Identity.Incarnation, blob, upload, Length);
        var request = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, Length, Zero);
        return await SubmitAsync(execution, OperationKind.BeginBlobUpload, request.CommandId, request,
            operation => new BlobUploadInfo(blob, upload, Length, Zero, Zero, Zero,
                operation.EvaluatedAt.AddSeconds(BlobLimits.DefaultUploadTtlSeconds), BlobUploadStatus.Active, initial), token);
    }

    private static async Task<ControlledBlobOutcomeLifetimeNativeProof> OneAsync(ControlledBlobOutcomeLifetimeNativeExecution execution,
        OperationKind kind, CancellationToken token)
    {
        var source = execution.Source;
        var blob = new BlobRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementBlobSeed.Resource, Prefix + kind);
        var upload = Guid.NewGuid();
        var initial = BlobIntegrity.InitialHash(execution.Target.Store.Identity.Incarnation, blob, upload, Length);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, Length, Zero);
        var begun = await SubmitAsync(execution, OperationKind.BeginBlobUpload, begin.CommandId, begin,
            operation => new BlobUploadInfo(blob, upload, Length, Zero, Zero, Zero,
                operation.EvaluatedAt.AddSeconds(BlobLimits.DefaultUploadTtlSeconds), BlobUploadStatus.Active, initial), token);
        if (kind == OperationKind.BeginBlobUpload)
        {
            return begun;
        }
        var info = begun.Expected.Get<BlobCommitResult<BlobUploadInfo>>().Value;
        if (kind is OperationKind.AbortBlobUpload or OperationKind.ReclaimBlob)
        {
            return await AbortedAsync(execution, kind, info, token);
        }
        var bytes = ControlledPartitionMovementBlobSeed.Bytes();
        var hash = BlobIntegrity.NextHash(initial, Zero, Length, ControlledPartitionMovementBlobSeed.PartSha256);
        var write = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, Zero, bytes,
            ControlledPartitionMovementBlobSeed.PartSha256);
        var written = await SubmitAsync(execution, OperationKind.WriteBlobPart, write.CommandId, write,
            _ => info with { NextOrdinal = One, StoredBytes = Length, IntegrityHash = hash }, token);
        if (kind == OperationKind.WriteBlobPart)
        {
            return written;
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = await SubmitAsync(execution, OperationKind.CompleteBlobUpload, complete.CommandId, complete,
            operation => new BlobMetadata(blob, One, upload, Length, One, hash, new RowAccess(), operation.EvaluatedAt), token);
        if (kind == OperationKind.CompleteBlobUpload)
        {
            return completed;
        }
        var delete = new DeleteBlobRequest(Guid.NewGuid(), blob, One);
        return await SubmitAsync(execution, OperationKind.DeleteBlob, delete.CommandId, delete,
            operation => new BlobMetadata(blob, DeletedRevision, null, Zero, Zero, null,
                new RowAccess(), operation.EvaluatedAt, true), token);
    }

    private static async Task<ControlledBlobOutcomeLifetimeNativeProof> AbortedAsync(ControlledBlobOutcomeLifetimeNativeExecution execution,
        OperationKind kind, BlobUploadInfo info, CancellationToken token)
    {
        var abort = new AbortBlobUploadRequest(Guid.NewGuid(), info.Blob, info.UploadId);
        var aborted = await SubmitAsync(execution, OperationKind.AbortBlobUpload, abort.CommandId, abort,
            _ => info with { Status = BlobUploadStatus.Aborted }, token);
        if (kind == OperationKind.AbortBlobUpload)
        {
            return aborted;
        }
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), info.Blob, info.UploadId, One);
        return await SubmitAsync(execution, OperationKind.ReclaimBlob, reclaim.CommandId, reclaim,
            _ => new BlobReclaimResult(info.UploadId, Zero, Zero, Zero, true), token);
    }

    private static async Task<ControlledBlobOutcomeLifetimeNativeProof> SubmitAsync<TRequest, TValue>(
        ControlledBlobOutcomeLifetimeNativeExecution execution, OperationKind kind, Guid id, TRequest request,
        Func<ReplicatedOperation, TValue> value, CancellationToken token)
    {
        var source = execution.Source;
        var target = execution.Target;
        var issuer = source.BorrowLifetimeIssuer(ControlledBlobOutcomeLifetimeNativeReads.MaximumBytes);
        var original = issuer.CreateNativeOperation(kind, id, "root",
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var index = checked(target.Journal.Log.State.LastIndex + One);
        var effect = await ControlledBlobOutcomeLifetimeNativeFlow.ApplyAsync(execution, original, token);
        await Assert.That(target.Journal.Log.State.CommittedIndex).IsEqualTo(index);
        var entry = target.Journal.Log.Read(index, One, target.Database.Limits.MaxBatchBytes).Single();
        await Assert.That(entry.Index).IsEqualTo(index);
        var committed = entry.Operation ?? throw new InvalidOperationException("The genuine Blob effect entry is absent.");
        await Assert.That(committed.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
        await Assert.That(committed.Id).IsEqualTo(effect.Receipt.CommandId);
        await Assert.That(committed.PrincipalId).IsEqualTo("root");
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition,
            ControlledPartitionMovementCorpus.Partition, original.PrincipalId, original.Id);
        var record = source.Store.Read(view => view.GetRecord<PartitionControlCommandRecord>(PartitionControlCommandKeys.Authority(identity)))
            ?? throw new InvalidOperationException("The committed source Blob command authority is absent.");
        await Assert.That(record.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        var retained = record.OriginalOperation ?? throw new InvalidOperationException("The committed original Blob command is absent.");
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(original, retained);
        var placement = source.Database.ReadAtomicPartitionPlacement("root",
            new(AtomicPartitionPlacementProtocol.CurrentVersion, ControlledPartitionMovementCorpus.Partition));
        var receipt = new CommitReceipt(id, new(target.Store.Identity.Incarnation,
            ControlledPartitionMovementCorpus.Partition.AtomicPartitionId, index, placement.PlacementEpoch), [], DurabilityProfile.ProcessDurable);
        var expected = new OperationResult(null) { NativeValue = new BlobCommitResult<TValue>(receipt, value(committed)) };
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(expected, effect.OriginalResult);
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(receipt with
        {
            CommandId = global::KeyLoad.Server.Features.DocumentStorage.ControlledDocumentTechnicalIdentity.Derive(original,
                global::KeyLoad.Server.Features.DocumentStorage.ControlledDocumentTechnicalIdentity.Effect)
        }, effect.Receipt);
        return new(original, expected);
    }
}
