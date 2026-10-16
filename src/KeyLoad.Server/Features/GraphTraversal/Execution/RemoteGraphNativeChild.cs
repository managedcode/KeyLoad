using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeChild
{
    internal static Task<ReadOnlyMemory<byte>> ReadAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, RemoteGraphNativeRequestV1 request, GrainReadKind kind,
        ReadExecutionBudget work, CancellationToken token, IResourceLifetimeWorkBinding binding) where T : class
    {
        work.RequireResourceLifetimeBinding(binding);
        work.Check();
        token.ThrowIfCancellationRequested();
        var childId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().RemoteGraph.CreateRemoteGraphRead(childId, principal.Id, kind,
            request.Payload, request.ExpiresAt);
        return ReadSignedAsync<T>(node, partition, principal, childId, signed, work, token, binding, null);
    }

    internal static async Task<ReadOnlyMemory<byte>> ReadSignedAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, Guid childId, string signed, ReadExecutionBudget work, CancellationToken token,
        IResourceLifetimeWorkBinding binding, ReadOnlyMemory<byte>? ownedInput) where T : class
    {
        work.RequireResourceLifetimeBinding(binding);
        var actual = await NativeLifetimeReadChildExecution.ExecuteAsync<T>(node, principal, childId, signed,
            ownedInput, token, binding, reply => RequireReply(reply, partition, work)).ConfigureAwait(false);
        work.Check();
        token.ThrowIfCancellationRequested();
        return RemoteGraphNativeResultEncoding.Encode(actual, partition.Database.Limits.MaxBatchBytes, work);
    }

    private static void RequireReply(GrainOperationReply reply, PartitionHost partition, ReadExecutionBudget work)
    {
        if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes || reply.Payload.Length > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        work.ChargeBytes(reply.Payload.Length);
    }

    internal static async Task<ReadOnlyMemory<byte>> ExecuteAsync<T>(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, Guid childId, Guid commandId, string signed, bool command,
        RemoteGraphEffectBoundary? boundary, ReadExecutionBudget work, CancellationToken token)
    {
        if (!command)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.InvalidProof);
        }
        GrainRequestIdentityScope? identity = null;
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                identity = node.OpenRequestContext(principal, childId, commandId, token);
                work.Check();
                token.ThrowIfCancellationRequested();
                boundary?.Begin(token);
                reply = await node.ExecuteAsync(childId, signed, command, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (identity is not null)
            {
                ServerFailureObserver.Observe(identity.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        work.Check();
        token.ThrowIfCancellationRequested();
        if (reply is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? RemoteGraphNativeProtocol.Unavailable);
        }
        if (reply.Payload.Length > partition.Database.Limits.MaxBatchBytes || reply.Payload.Length > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        work.ChargeBytes(reply.Payload.Length);
        if (GrainNativePayload.Read<GrainValue>(reply.Payload).Value is not T actual)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
        }
        return RemoteGraphNativeResultEncoding.Encode(actual, partition.Database.Limits.MaxBatchBytes, work);
    }
}
