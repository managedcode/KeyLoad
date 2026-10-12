using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphFreshIncomingObservation
{
    internal static async Task<RemoteGraphIncomingFinalizeRequestV1> RefreshAsync(OrleansNode node,
        PartitionHost partition, RemoteGraphNativeRequestV1 request, RemoteGraphIncomingFinalizeRequestV1 original,
        ReadExecutionBudget work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        work.Check();
        token.ThrowIfCancellationRequested();
        if (original.Candidates?.Request is null || original.Candidates.States.IsDefault)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof); }
        var actual = partition.Database.ReadRemoteGraphReceiverCandidates(request.PrincipalId,
            original.Candidates.Request, work);
        if (!partition.Database.RemoteGraphIncomingBytesEqual(actual, original.Candidates, work))
        { throw Errors.Fail(ErrorCode.Conflict, RemoteGraphNativeProtocol.InvalidProof); }
        var directory = ReadDirectory(partition, work);
        var proofs = ImmutableArray.CreateBuilder<RemoteGraphIncomingSourceProofV1>();
        foreach (var state in actual.States)
        {
            work.Check();
            token.ThrowIfCancellationRequested();
            if (state.Content.Deleted)
            { continue; }
            var source = directory.Owners.SingleOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.Owner, state.Intent.SourceOwner))
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
            var proof = await ReadProofAsync(node, partition, request, source, state.Intent, work, token)
                .ConfigureAwait(false);
            if (proof is not null)
            { proofs.Add(proof); }
        }
        return new(actual, proofs.ToImmutable());
    }

    private static PhysicalOwnerDirectoryV1 ReadDirectory(PartitionHost partition, ReadExecutionBudget work)
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
            return directory;
        });

    private static async Task<RemoteGraphIncomingSourceProofV1?> ReadProofAsync(OrleansNode node,
        PartitionHost partition, RemoteGraphNativeRequestV1 request, RegisteredPhysicalOwnerV1 source,
        RemoteGraphIntentV1 intent, ReadExecutionBudget work, CancellationToken token)
    {
        var bytes = RemoteGraphNativeResultEncoding.Encode(new RemoteGraphIncomingSourceProofRequestV1(intent),
            partition.Database.Limits.MaxBatchBytes, work);
        RemoteGraphNativeReplyV1? reply = null;
        try
        {
            reply = await node.BorrowRemoteGraphClient().SendIncomingProofReplyAsync(source,
                request.PrincipalId, bytes, Guid.NewGuid(), request.ExpiresAt, work, token).ConfigureAwait(false);
            work.Check();
            token.ThrowIfCancellationRequested();
            if (reply.Error is ErrorCode.PermissionDenied or ErrorCode.NotFound)
            { return null; }
            if (reply.Error is { } refusal)
            { throw Errors.Fail(refusal, RemoteGraphNativeProtocol.Unavailable); }
            if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes
                || reply.Payload.Length > work.MaximumResultBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
            work.ChargeBytes(reply.Payload.Length);
            var proof = NativeSerialization.Deserialize<RemoteGraphIncomingSourceProofV1>(reply.Payload.Span);
            if (proof is null || proof.Version != RemoteGraphNativeProtocol.Version || proof.SourceCut is null
                || !partition.Database.RemoteGraphIncomingBytesEqual(proof.Intent, intent, work))
            { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
            return proof;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
            if (reply is not null)
            { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Payload).Span); }
        }
    }
}
