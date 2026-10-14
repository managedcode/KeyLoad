using KeyLoad.Clustering;
using KeyLoad.Core;

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
        using var identity = node.OpenRequestContext(actualPrincipal, requestId, Guid.Empty, originalCancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: false,
            cancellationToken: originalCancellationToken).ConfigureAwait(false);
        authority.Require(originalCall, originalCancellationToken);
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, EventVectorRemoteProtocol.Unavailable);
        }
        work.ChargeBytes(reply.Payload.Length);
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value is T result ? result
            : throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
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
        using var identity = node.OpenRequestContext(actualPrincipal, requestId, operation.Id, originalCancellationToken);
        boundary?.Begin(originalCancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: true,
            cancellationToken: originalCancellationToken).ConfigureAwait(false);
        authority.Require(originalCall, originalCancellationToken);
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, EventVectorRemoteProtocol.Unavailable);
        }
    }
}
