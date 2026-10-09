using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns issuance responsibility while borrowing the single native receiver owner and original operation tokens.</summary>
internal sealed class PartitionMovementReceiverIssuance(PartitionMovementReceiverContext context, PartitionMovementReceiverReads reads)
{
    internal async Task<PartitionMoveReceiverIssuanceWitness> ReadReceiverIssuanceProofAsync(
        PartitionMovementReceiverIssueQuery requested, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var principal = context.Administrator(cancellationToken);
        await context.Partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var grant = work.CreateReadGrant(requested.MaximumReadBytes, requested.MaximumExaminedRecords);
        var query = new PartitionMovementReceiverIssuanceQuery(requested.OriginalPhaseCommandId,
            requested.OriginalEnvelope, requested.OriginalAuthorization, grant.RemainingBytes,
            grant.RemainingRecords, Math.Min(requested.MaximumResultBytes, work.MaximumResultBytes));
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementReceiverIssuance(requestId, principal.Id,
            query, requested.QueryExpiresAt);
        var terminal = await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
        if (terminal.Error is { } code)
        { throw Errors.Fail(code, terminal.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(terminal.Payload).Value as PartitionMovementReceiverIssuanceResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(grant, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(grant);
        var reply = new PartitionMovementTransportReply(requested.OriginalPhaseCommandId, requested.QueryNonce,
            context.LocalOwner(), context.Discovery(), terminal, actual.Snapshot.Issuance.OriginalPhaseIdentityDigest);
        var encoded = NativeSerialization.Serialize(reply);
        if (encoded.Length > context.Partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        return SignReceiverIssuanceProof(requested, encoded, work);
    }

    internal PartitionMoveReceiverIssuanceWitness SignReceiverIssuanceProof(PartitionMovementReceiverIssueQuery requested,
        ReadOnlyMemory<byte> encoded, ReadExecutionBudget work)
    {
        var key = Convert.FromBase64String(context.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            var witness = new PartitionMoveReceiverIssuanceWitness(PartitionMoveProtocol.Version,
                requested.OriginalPhaseCommandId, requested.QueryNonce, encoded, mac.SignReceiverIssue(encoded.Span, source: false));
            work.MeasureResult(witness);
            work.Check();
            return witness;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal Task<GrainOperationReply> IssueReceiverAsync(PartitionMovementReceiverIssueRequest request,
        ReadOnlyMemory<byte> originalBytes, string originalSignature, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var body = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version,
            request.OriginalPhaseCommandId, request.OriginalEnvelope, request.OriginalAuthorization,
            request.SourceWitness.OriginalReplyBytes, SourcePendingSignature: request.SourceWitness.OriginalReplySignature,
            OriginalIssuerRequestBytes: originalBytes, OriginalIssuerRequestSignature: originalSignature);
        return IssueReceiverAsync(request.IssuanceCommandId, body, work, cancellationToken);
    }

    internal async Task<GrainOperationReply> IssueReceiverAsync(Guid commandId, PartitionMoveReceiverIssueBody body,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var principal = context.Administrator(cancellationToken);
        var operation = context.Partition.Database.CreateVerifiedPartitionMovementReceiverIssue(commandId, principal.Id, body, work);
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementCommand(requestId, operation,
            body.OriginalEnvelope.ExpiresAt);
        var actual = await context.ExecuteSignedAsync(principal, requestId, commandId, signed, command: true,
            cancellationToken).ConfigureAwait(false);
        work.Check();
        return actual;
    }

    internal async Task<PartitionMoveReceiverIssuePacket> CreateReceiverIssuePacketAsync(string principalId,
        PartitionMoveRequest request, Guid phaseId, DateTimeOffset readExpiry, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var state = await reads.ReadParentStateAsync(principalId, request, phaseId, readExpiry, work,
            cancellationToken).ConfigureAwait(false);
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        if (!PhysicalOwnerEntryValidation.SameOwner(context.LocalOwner(), header.ControlOwner)
            || header.OperatorPrincipalId != principalId || state.CurrentOperatorPrincipalId != principalId
            || pending.OriginalPhaseCommandId != phaseId || pending.OriginalResult is not null
            || pending.OriginalReceiverIssuePacket is not null || pending.ReceiverIssuePacketCheckpointReceipt is not null
            || pending.OriginalGrant is not { RequireReceiverIssuance: true } grant
            || state.CurrentOperatorPolicyEpoch != grant.OperatorPolicyEpoch
            || grant.ExpiresAt <= context.Clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementIssuerSourceAckVerifier.Require(context.Partition.Database, principalId, state, pending, work);
        var first = CreateReceiverIssuerRequest(pending);
        var bytes = NativeSerialization.Serialize(first);
        if (bytes.Length > context.Partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        return SignReceiverIssuerPacket(first, bytes, work);
    }

    internal PartitionMovementReceiverIssueRequest CreateReceiverIssuerRequest(PartitionMoveParentPhase pending)
    {
        var phase = pending.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var sourceWitness = pending.OriginalReceiverSourceWitness
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var sourceAck = pending.ReceiverSourceCheckpointReceipt
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = pending.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner,
            phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal,
            pending.OriginalExpiresAt, pending.OriginalRequestNonce, phase.Body, pending.OriginalGrant);
        var discovery = context.Discovery();
        var nonce = PartitionMovementReceiverIssueNonce.For(pending.OriginalPhaseCommandId, sourceWitness.QueryNonce);
        if (nonce == pending.OriginalRequestNonce || nonce == sourceWitness.QueryNonce
            || sourceAck.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition || !phase.ControlOwner.VoterIds.Contains(discovery.VoterId)
            || !PhysicalOwnerEntryValidation.SameOwner(sourceAck.PhysicalOwner, phase.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.InvalidProof); }
        return new(PartitionMoveProtocol.Version, PartitionMoveReceiverIssuanceIdentity.For(pending.OriginalPhaseCommandId),
            pending.OriginalPhaseCommandId, envelope, sourceWitness, authorization, discovery.VoterId,
            discovery.SiloAddress, nonce, sourceAck);
    }

    internal PartitionMoveReceiverIssuePacket SignReceiverIssuerPacket(PartitionMovementReceiverIssueRequest requested,
        ReadOnlyMemory<byte> bytes, ReadExecutionBudget work)
    {
        var key = Convert.FromBase64String(context.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            var packet = new PartitionMoveReceiverIssuePacket(PartitionMoveProtocol.Version,
                requested.OriginalPhaseCommandId, requested.IssuanceNonce, bytes,
                mac.SignReceiverIssueRequest(bytes.Span, query: false));
            work.MeasureResult(packet);
            work.Check();
            return packet;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
