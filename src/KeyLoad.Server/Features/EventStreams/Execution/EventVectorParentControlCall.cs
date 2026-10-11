using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorParentControlCall
{
    internal static async Task<GrainOperationReply> ExecuteReplyAsync(OrleansNode node, PartitionHost partition,
        TimeProvider clock, DecodedGrainRequest parent, EventVectorControlPhase phase,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(work);
        originalCancellationToken.ThrowIfCancellationRequested();
        var envelope = parent.Envelope;
        var original = GrainNativePayload.ReadCommand<EventFeedControlRequest>(parent.Payload);
        if (envelope.Purpose != EventVectorRequestScope.ParentPurpose
            || envelope.CommandKind != OperationKind.EventFeedControl || envelope.ReadKind is not null
            || envelope.CommandId != original.CommandId || original != phase.OriginalRequest
            || envelope.ExpiresAt != phase.OriginalParentExpiresAt || envelope.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Invalid); }
        if (phase.OriginalParentRequestId != Guid.Empty
            && phase.OriginalParentRequestId != envelope.RequestId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Invalid); }
        var database = partition.Database;
        var principal = GrainRequestAuthority.ReloadForRequest(database, envelope, clock);
        var owner = database.ReadEventVectorControlOwner(principal.Id, original, work, originalCancellationToken);
        if (owner.Incarnation != database.Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        var operation = database.CreateVerifiedEventVectorControlPhase(principal.Id, phase, work);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateEventVectorPhase(requestId, operation,
            envelope.ExpiresAt);
        using var context = node.OpenRequestContext(principal, requestId, operation.Id, originalCancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: true,
            cancellationToken: originalCancellationToken).ConfigureAwait(false);
        var transferred = false;
        try
        {
            work.Check();
            if (reply.Error is { } failure)
            { throw Errors.Fail(failure, EventVectorRemoteProtocol.Unavailable); }
            work.ChargeBytes(reply.Payload.Length);
            transferred = true;
            return reply;
        }
        finally
        {
            if (!transferred)
            { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Payload).Span); }
        }
    }

    internal static async Task<object> ExecuteAsync(OrleansNode node, PartitionHost partition,
        TimeProvider clock, DecodedGrainRequest parent, EventVectorControlPhase phase,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var reply = await ExecuteReplyAsync(node, partition, clock, parent, phase, work,
            originalCancellationToken).ConfigureAwait(false);
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value;
        return value is EventVectorControlResult or EventVectorFirstIssuanceResult
            or KeyLoad.Replication.EventVectorFirstAdmitAppendResult ? value
            : throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }

    internal static async Task SubmitWithoutDecodingAsync(OrleansNode node, PartitionHost partition,
        TimeProvider clock, DecodedGrainRequest parent, EventVectorControlPhase phase,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var reply = await ExecuteReplyAsync(node, partition, clock, parent, phase, work,
            originalCancellationToken).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Payload).Span);
    }
}
