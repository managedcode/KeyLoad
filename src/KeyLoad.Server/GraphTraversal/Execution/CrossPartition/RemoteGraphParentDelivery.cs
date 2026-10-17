using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphParentDelivery
{
    internal static async Task<RemoteGraphEdgeResultV1> ExecuteAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainRequestEnvelope trusted, Guid publicCommandId, RemoteGraphOwnerCutV1 sourceCut,
        RemoteGraphSourceResultV1 source, RemoteGraphEffectBoundary effects, ReadExecutionBudget work,
        CancellationToken token)
    {
        if (source?.Original?.Original is null || source.Receipt is null || source.Intents.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
        }
        var results = ImmutableArray.CreateBuilder<RemoteGraphDeliveryResultV1>(source.Intents.Length);
        foreach (var intent in source.Intents)
        {
            work.Check();
            token.ThrowIfCancellationRequested();
            var receiveId = RemoteGraphPhaseCommandIdentity.Create(intent, OperationKind.RemoteGraphReceive, partition, work);
            var proof = new RemoteGraphSourceProofV1(RemoteGraphProtocol.Version, intent, sourceCut,
                principal.Id, source.Original.Original);
            var mutation = new RemoteGraphReceiverMutationV1(RemoteGraphProtocol.Version, receiveId, proof, string.Empty);
            var received = await RemoteGraphParentCall.RemoteAsync<RemoteGraphReceiverResultV1>(node, partition,
                principal, intent.DestinationOwner, RemoteGraphNativeAction.ReceiverApply, mutation,
                trusted.ExpiresAt, work, token).ConfigureAwait(false);
            RequireReceipt(received, intent, receiveId, partition, work);
            var completeId = RemoteGraphPhaseCommandIdentity.Create(intent, OperationKind.RemoteGraphComplete, partition, work);
            var completion = new RemoteGraphCompletionMutationV1(RemoteGraphProtocol.Version, completeId,
                received.Receipt, string.Empty);
            var operation = partition.Database.CreateVerifiedRemoteGraphComplete(principal.Id,
                completion, trusted.ExpiresAt, work);
            var completed = await RemoteGraphParentCall.CommandAsync<RemoteGraphCompletionResultV1>(node, partition,
                principal, operation, trusted.ExpiresAt, effects, work, token).ConfigureAwait(false);
            if (completed?.Original is null || completed.Receipt?.CommandId != completeId
                || completed.Original.CommandId != completeId
                || !Same(completed.Original.Receipt, received.Receipt, partition, work))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
            }
            results.Add(new(intent.Content.Destination, intent.Content.Revision, intent.Content.Deleted,
                received.Receipt.TargetReceipt, completed.Receipt));
        }
        return new(publicCommandId, source.Receipt, results.MoveToImmutable());
    }

    private static void RequireReceipt(RemoteGraphReceiverResultV1 actual, RemoteGraphIntentV1 intent,
        Guid expectedId, PartitionHost partition, ReadExecutionBudget work)
    {
        if (actual?.Original?.SourceProof?.Intent is null || actual.Receipt?.Intent is null
            || actual.Receipt.TargetReceipt?.CommandId != expectedId || actual.Original.CommandId != expectedId
            || !Same(actual.Receipt.Intent, intent, partition, work)
            || !Same(actual.Original.SourceProof.Intent, intent, partition, work))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
        }
    }

    private static bool Same<T>(T left, T right, PartitionHost partition, ReadExecutionBudget work)
    {
        var a = RemoteGraphNativeResultEncoding.Encode(left, partition.Database.Limits.MaxBatchBytes, work);
        try
        {
            var b = RemoteGraphNativeResultEncoding.Encode(right, partition.Database.Limits.MaxBatchBytes, work);
            try
            {
                return a.Span.SequenceEqual(b.Span);
            }
            finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(b).Span); }
        }
        finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(a).Span); }
    }
}
