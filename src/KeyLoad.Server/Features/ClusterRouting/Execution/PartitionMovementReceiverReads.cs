using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns reads responsibility while borrowing the single native receiver owner and original operation tokens.</summary>
internal sealed class PartitionMovementReceiverReads(PartitionMovementReceiverContext context)
{
    internal async Task<OperationResult> ReadParentOriginalOutcomeAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentPhase original, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var state = await ReadParentStateAsync(principalId, request, original.OriginalPhaseCommandId,
            expiry, work, cancellationToken).ConfigureAwait(false);
        var canonical = state.Selected
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        if (!NativeSerialization.Serialize(canonical).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.InvalidProof); }
        return state.SelectedOriginalOutcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.Unavailable);
    }

    internal async Task<PartitionMoveParentState> ReadParentStateAsync(string principalId, PartitionMoveRequest request,
        Guid selectedPhaseId, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        await context.Partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var principal = context.Administrator(principalId, cancellationToken);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var query = new PartitionMovementParentStateQuery(request, selectedPhaseId,
            grant.RemainingBytes, grant.RemainingRecords, work.MaximumResultBytes);
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementParentState(requestId, principal.Id, query, expiry);
        var reply = await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } code)
        { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(grant, value.ReadBytes, value.ExaminedRecords);
        work.CompleteReadGrant(grant);
        work.MeasureResult(value);
        work.Check();
        return value.State;
    }

    internal async Task<PartitionMoveReceiverSourceWitness> ReadReceiverSourcePendingAsync(string principalId,
        PartitionMoveRequest request, Guid phaseId, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        await context.Partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var principal = context.Administrator(principalId, cancellationToken);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var query = new PartitionMovementParentStateQuery(request, phaseId,
            grant.RemainingBytes, grant.RemainingRecords, work.MaximumResultBytes);
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementParentState(requestId, principal.Id, query, expiry);
        var terminal = await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
        if (terminal.Error is { } code)
        { throw Errors.Fail(code, terminal.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(terminal.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        var pending = actual.State.Pending;
        if (pending?.OriginalPhaseCommandId != phaseId || pending.OriginalResult is not null
            || actual.State.CurrentOperatorPrincipalId != principal.Id || actual.State.CurrentOperatorPolicyEpoch <= PartitionMovementProtocol.UnissuedPolicyEpoch)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        work.ImportReadGrant(grant, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(grant);
        var nonce = Guid.NewGuid();
        var reply = new PartitionMovementSourcePendingReply(PartitionMoveProtocol.Version, requestId, nonce,
            expiry, context.LocalOwner(), actual.State.CurrentOperatorPrincipalId,
            actual.State.CurrentOperatorPolicyEpoch, terminal, context.Discovery());
        var encoded = NativeSerialization.Serialize(reply);
        if (encoded.Length > context.Partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(context.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            var witness = new PartitionMoveReceiverSourceWitness(PartitionMoveProtocol.Version, phaseId,
                nonce, encoded, mac.SignReceiverIssue(encoded, source: true));
            work.MeasureResult(witness);
            work.Check();
            return witness;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal async Task<PartitionMovementAuthenticatedAuthority> ReadTransferAuthorityAsync(string principalId,
        PartitionMovementTransferAuthorityQuery query, DateTimeOffset expiry, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        work.Check();
        await context.Partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var principal = context.Administrator(principalId, cancellationToken);
        var leaf = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var bounded = query with
        {
            MaximumReadBytes = Math.Min(query.MaximumReadBytes, leaf.RemainingBytes),
            MaximumExaminedRecords = Math.Min(query.MaximumExaminedRecords, leaf.RemainingRecords),
            MaximumResultBytes = Math.Min(query.MaximumResultBytes, work.MaximumResultBytes)
        };
        if (bounded.MaximumReadBytes <= PartitionMovementProtocol.NoReadBytes || bounded.MaximumExaminedRecords <= PartitionMovementProtocol.NoExaminedRecords || bounded.MaximumResultBytes <= PartitionMovementProtocol.NoResultBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var requestId = Guid.NewGuid();
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementTransferAuthority(requestId, principal.Id, bounded, expiry);
        var terminal = await context.ExecuteSignedAsync(principal, requestId, Guid.Empty, signed, command: false,
            cancellationToken).ConfigureAwait(false);
        if (terminal.Error is { } code)
        { throw Errors.Fail(code, terminal.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var result = GrainNativePayload.Read<GrainValue>(terminal.Payload).Value as PartitionMovementTransferAuthorityResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(leaf, result.ReadBytes, result.ExaminedRecords);
        work.CompleteReadGrant(leaf);
        work.MeasureResult(result);
        work.Check();
        var original = result.Authority;
        var reply = new PartitionMovementTransferAuthorityReply(PartitionMoveProtocol.Version, requestId,
            Guid.NewGuid(), expiry, context.LocalOwner(), original.Header.OriginalSourceOwner
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof), context.Discovery(), terminal);
        if (NativeSerialization.Measure(reply) > context.Partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var encoded = NativeSerialization.Serialize(reply);
        var key = Convert.FromBase64String(context.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            return new(encoded, mac.SignTransferAuthority(encoded));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal Task<GrainOperationReply> ExecuteTransferDataAsync(PartitionMovementTransferDataRequest request,
        CancellationToken cancellationToken)
    {
        var principal = context.Administrator(request.OperatorPrincipalId, cancellationToken);
        var signed = context.Node.CatalogRequestCodec().CreatePartitionMovementTransferData(request.RequestId,
            principal.Id, request.Capability, request.ExpiresAt);
        return context.ExecuteSignedAsync(principal, request.RequestId, Guid.Empty, signed, command: false, cancellationToken);
    }
}
