using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphFreshSourceObservation
{
    internal static async Task<RemoteGraphReceiverMutationV1> RefreshAsync(OrleansNode node,
        PartitionHost partition, RemoteGraphNativeRequestV1 request, RemoteGraphReceiverMutationV1 mutation,
        ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        var proof = mutation.SourceProof;
        if (proof is null || proof.Original is null || proof.Intent is null || proof.Intent.Content is null
            || proof.Intent.SourceOwner is null || proof.Original.CommandId != proof.Intent.SourceCommandId
            || proof.Original.SourcePartition != proof.Intent.Content.SourcePartition)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        var source = ReadSource(partition, proof.Intent.SourceOwner, work);
        var original = new RemoteGraphSourceReadRequestV1(proof.Intent.SourceCommandId,
            proof.Intent.Content.SourcePartition, proof.Intent.Content.Graph, proof.Intent.Content.EdgeId,
            source.Owner, proof.Original);
        var bytes = RemoteGraphNativeResultEncoding.Encode(original, partition.Database.Limits.MaxBatchBytes, work);
        RemoteGraphNativeReplyV1? reply = null;
        try
        {
            reply = await node.BorrowRemoteGraphClient().SendAsync(source, RemoteGraphNativeAction.SourceIntent,
                request.PrincipalId, bytes, Guid.NewGuid(), request.ExpiresAt, work, token).ConfigureAwait(false);
            work.Check();
            token.ThrowIfCancellationRequested();
            if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes || reply.Payload.Length > work.MaximumResultBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
            work.ChargeBytes(reply.Payload.Length);
            var snapshot = NativeSerialization.Deserialize<RemoteGraphSourceSnapshotV1>(reply.Payload.Span);
            return SelectActual(partition, request.PrincipalId, mutation, original, snapshot, work);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
            if (reply is not null)
            { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Payload).Span); }
        }
    }

    private static RegisteredPhysicalOwnerV1 ReadSource(PartitionHost partition,
        PhysicalShardRecord expected, ReadExecutionBudget work)
        => partition.Database.Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            var directory = PhysicalOwnerDirectorySerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
            PhysicalOwnerDirectoryValidation.Validate(directory);
            if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
            { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable); }
            var selected = directory.Owners.SingleOrDefault(value => value.Owner.PhysicalShardId == expected.PhysicalShardId);
            if (selected is null || !PhysicalOwnerEntryValidation.SameOwner(selected.Owner, expected))
            { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable); }
            return selected;
        });

    private static RemoteGraphReceiverMutationV1 SelectActual(PartitionHost partition, string principalId,
        RemoteGraphReceiverMutationV1 mutation, RemoteGraphSourceReadRequestV1 original,
        RemoteGraphSourceSnapshotV1 snapshot, ReadExecutionBudget work)
    {
        var maximum = partition.Database.Limits.MaxBatchBytes;
        if (snapshot is null || snapshot.Request is null || snapshot.Cut is null
            || snapshot.Cut.Owner is null || !PhysicalOwnerEntryValidation.SameOwner(snapshot.Cut.Owner, original.SourceOwner)
            || !Same(snapshot.Request, original, maximum, work))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        var stored = snapshot.OriginalOutcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphNativeProtocol.Unavailable);
        var actual = stored.Result.Get<RemoteGraphSourceResultV1>();
        if (actual.Original is null || actual.Receipt is null || actual.Intents.IsDefault
            || actual.Receipt.CommandId != original.OriginalCommandId
            || !Same(actual.Original.Original, original.Original, maximum, work))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        RemoteGraphIntentV1? selected = null;
        foreach (var intent in actual.Intents)
        {
            work.Check();
            if (!Same(intent, mutation.SourceProof.Intent, maximum, work))
            { continue; }
            if (selected is not null)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof); }
            selected = intent;
        }
        if (selected is null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        return mutation with
        {
            SourceProof = new RemoteGraphSourceProofV1(
            RemoteGraphNativeProtocol.Version, selected, snapshot.Cut, principalId, actual.Original.Original)
        };
    }

    private static bool Same<T>(T left, T right, int maximum, ReadExecutionBudget work)
    {
        var first = RemoteGraphNativeResultEncoding.Encode(left, maximum, work);
        try
        {
            var second = RemoteGraphNativeResultEncoding.Encode(right, maximum, work);
            try
            { return first.Span.SequenceEqual(second.Span); }
            finally { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(second).Span); }
        }
        finally { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(first).Span); }
    }
}
