using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorNativeReceivingCall(OrleansNode node, PartitionHost partition,
    EventVectorRemoteCallValidation authority, TimeProvider clock)
{
    internal PrincipalRecord Principal(string principalId, CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        return partition.Database.Store.Read(view => partition.Database.Principal(view, principalId, clock.GetUtcNow()));
    }

    internal async Task<T> ReadAsync<T>(EventVectorRemoteCall originalCall, PrincipalRecord actualPrincipal,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        authority.Require(originalCall, originalCancellationToken);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateEventVectorRead(requestId, actualPrincipal.Id,
            kind, payload, originalCall.ExpiresAt);
        var result = await NativeLifetimeReadChildExecution.ExecuteAsync<object>(node, actualPrincipal,
            requestId, signed, null, originalCancellationToken, work.RequireAttachedResourceLifetimeBinding(), reply =>
            {
                authority.Require(originalCall, originalCancellationToken);
                if (reply.Error is { } failure)
                {
                    throw Errors.Fail(failure, EventVectorRemoteProtocol.Unavailable);
                }
                work.ChargeBytes(reply.Payload.Length);
            }).ConfigureAwait(false);
        return result is T value ? value : throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }

    internal Task SubmitAsync(EventVectorRemoteCall originalCall, PrincipalRecord actualPrincipal,
        EventVectorSourcePhase originalPhase, ReadExecutionBudget work, CancellationToken originalCancellationToken)
        => SubmitAsync(originalCall, actualPrincipal, originalPhase, work, null, originalCancellationToken);

    internal async Task SubmitAsync(EventVectorRemoteCall originalCall, PrincipalRecord actualPrincipal,
        EventVectorSourcePhase originalPhase, ReadExecutionBudget work, global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeEffectBoundary? boundary,
        CancellationToken originalCancellationToken)
    {
        authority.Require(originalCall, originalCancellationToken);
        var database = partition.Database;
        work.Check();
        var operation = database.CreateVerifiedEventVectorSourcePhase(actualPrincipal.Id, originalPhase, work);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateEventVectorPhase(requestId, operation,
            originalPhase.FirstExpiresAt);
        var reply = await EventVectorNativeChild.ExecuteAsync(node, actualPrincipal, requestId, operation.Id,
            signed, work, boundary, originalCancellationToken).ConfigureAwait(false);
        authority.Require(originalCall, originalCancellationToken);
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, EventVectorRemoteProtocol.Unavailable);
        }
    }
}
