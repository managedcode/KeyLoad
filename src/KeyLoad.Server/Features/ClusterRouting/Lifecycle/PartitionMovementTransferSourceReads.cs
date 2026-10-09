using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

/// <summary>Fresh source reads share the original owner's producer, image and abort/shutdown registries.</summary>
internal sealed partial class PartitionMovementTransferSourceReads(Lock gate,
    Dictionary<Guid, IPartitionMovementRetainedSourceImage> sessions,
    Dictionary<Guid, IPartitionMovementPendingSourceRead> captures,
    Dictionary<PartitionMovementSourceScope, IPartitionMovementRetainedSourceImage[]> closedMoves,
    DatabaseEngine database, ICacheMemoryBudget memory, NativeRequestWorkOwner workOwner,
    Action requireOpen, Action<PartitionRef, Guid> requireMoveOpen,
    CancellationToken shutdown) : INativePartitionMovementTransferRead
{
    public async Task<PartitionMovementTransferHandle> OpenAsync(PrincipalRecord principal,
        PartitionMovementTransferDataCapability query, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        query = PartitionMovementTransferSourceScope.Own(query, database.Limits, PartitionMovementTransferDataAction.Open);
        GrainRequestAuthority.RequireAdministrator(principal);
        work.Check();
        var authority = database.VerifyPartitionMovementTransferReadAuthority(principal.Id,
            query.OriginalAuthorityReplyBytes, query.OriginalAuthoritySignature, work);
        var reply = NativeSerialization.Deserialize<PartitionMovementTransferAuthorityReply>(query.OriginalAuthorityReplyBytes.Span);
        PartitionMovementPendingTransferRead? producer = null;
        Task<PartitionMovementTransferHandle>? retained = null;
        PartitionMovementTransferSourceEntry? existing = null;
        lock (gate)
        {
            requireMoveOpen(authority.Header.Partition, authority.Header.MoveId);
            if (captures.TryGetValue(reply.RequestId, out var pending))
            {
                if (pending is not PartitionMovementPendingTransferRead original)
                { throw Errors.Fail(ErrorCode.Conflict, PartitionMovementProtocol.InvalidProof); }
                retained = original.Completion.Task;
            }
            else
            {
                existing = sessions.Values.OfType<PartitionMovementTransferSourceEntry>()
                    .FirstOrDefault(value => value.RequestId == reply.RequestId);
                if (existing is null)
                {
                    producer = new(authority.Header.Partition, authority.Header.MoveId);
                    captures.Add(reply.RequestId, producer);
                }
            }
        }
        if (producer is not null)
        { CompleteOpen(principal, query, reply.RequestId, producer, work); }
        var handle = existing?.Handle ?? await (retained ?? producer!.Completion.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        Find(principal, query with { HandleId = handle.HandleId }, cancellationToken).Session.RequireOpen();
        return handle;
    }
}
