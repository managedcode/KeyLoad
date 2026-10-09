using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementRetireCancellationPeerAdmission
{
    internal static async Task<PartitionMovementRetireCancellationRequest> VerifyRetireCancellationAsync(PartitionMovementPeerAdmission peer,
        ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken)
    {
        var bytes = VerifyRetireCancellationMac(peer, body, signature, query: false);
        var request = NativeSerialization.Deserialize<PartitionMovementRetireCancellationRequest>(bytes);
        if (request.Version != PartitionMoveProtocol.Version || request.CancellationCommandId == Guid.Empty
            || request.CancellationCommandId == request.OriginalPhaseCommandId
            || request.CleanupGeneration < PartitionMoveProtocol.EmptyCount
            || request.OriginalEnvelope.Stage != PartitionMovePeerStage.Retire
            || request.OriginalEnvelope.ExpiresAt > peer.Clock.GetUtcNow()
            || request.SourceWitness.OriginalPhaseCommandId != request.OriginalPhaseCommandId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await PartitionMovementReceiverIssuePeerAdmission.RequireReceiverIssuePeerAsync(peer, request.OriginalPhaseCommandId, request.OriginalEnvelope,
            request.CallerVoter, request.CallerSiloAddress, request.CancellationNonce,
            request.CancellationExpiresAt, cancellationToken).ConfigureAwait(false);
        return request;
    }

    internal static async Task<PartitionMovementRetireCancellationQuery> VerifyRetireCancellationQueryAsync(PartitionMovementPeerAdmission peer,
        ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken)
    {
        var bytes = VerifyRetireCancellationMac(peer, body, signature, query: true);
        var request = NativeSerialization.Deserialize<PartitionMovementRetireCancellationQuery>(bytes);
        if (request.Version != PartitionMoveProtocol.Version || request.CancellationCommandId == Guid.Empty
            || request.CancellationCommandId == request.OriginalPhaseCommandId
            || request.OriginalEnvelope.Stage != PartitionMovePeerStage.Retire
            || request.MaximumReadBytes <= PartitionMoveProtocol.EmptyCount
            || request.MaximumReadBytes > peer.Database.Limits.MaxQueryReadBytes
            || request.MaximumExaminedRecords <= PartitionMoveProtocol.EmptyCount
            || request.MaximumExaminedRecords > peer.Database.Limits.MaxScanRecords
            || request.MaximumResultBytes <= PartitionMoveProtocol.EmptyCount
            || request.MaximumResultBytes > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        await PartitionMovementReceiverIssuePeerAdmission.RequireReceiverIssuePeerAsync(peer, request.OriginalPhaseCommandId, request.OriginalEnvelope,
            request.CallerVoter, request.CallerSiloAddress, request.QueryNonce,
            request.QueryExpiresAt, cancellationToken).ConfigureAwait(false);
        return request;
    }

    internal static byte[] VerifyRetireCancellationMac(PartitionMovementPeerAdmission peer, ReadOnlyMemory<byte> body, string signature, bool query)
    {
        if (!peer.Execution.Enabled || !peer.Options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof); }
        if (body.IsEmpty || body.Length > peer.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        var owned = body.ToArray();
        using var mac = peer.CreateControlMac();
        if (!mac.VerifyRetireCancellationRequest(owned, signature, query))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof); }
        return owned;
    }
}
