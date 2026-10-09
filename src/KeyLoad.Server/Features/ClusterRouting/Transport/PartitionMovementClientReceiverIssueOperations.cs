using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientReceiverIssueOperations
{
    internal static async Task IssueReceiverFirstAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        await owner.partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var current = PartitionMovementClientReceiverIssueOperations.ReadCurrentReceiverIssueSource(owner, original, work);
        if (current.CurrentOperatorPolicyEpoch != original.OriginalGrant!.OperatorPolicyEpoch
            || original.OriginalExpiresAt <= owner.clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMovementProtocol.InvalidProof); }
        var packet = original.OriginalReceiverIssuePacket
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        if (original.ReceiverIssuePacketCheckpointReceipt is null || original.OriginalResult is not null
            || original.OriginalReceiverIssuanceWitness is not null || original.ReceiverIssuanceCheckpointReceipt is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var first = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(packet.OriginalRequestBytes.Span);
        if (first.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || packet.IssuanceNonce != first.IssuanceNonce
            || PartitionMoveOriginalDispatchIdentity.Digest(first.OriginalPhaseCommandId, first.OriginalEnvelope)
                != original.OriginalPhaseIdentityDigest)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var selected = PartitionMovementClientReceiverIssueOperations.ReceiverIssueOwner(owner, original);
        var local = PhysicalOwnerEntryValidation.SameOwner(selected.Owner, first.OriginalEnvelope.ControlOwner);
        var scope = new PartitionMovementTransportRequest(original.OriginalPhaseCommandId,
            first.OriginalEnvelope with { Nonce = first.IssuanceNonce }, first.OriginalAuthorization,
            first.CallerVoter, first.CallerSiloAddress, PartitionMovementTransportAction.Apply, Guid.Empty, PartitionMovementClient.FirstVoter);
        var exchange = new PartitionMovementReceiverExchange(scope, original.OriginalPhaseIdentityDigest,
            packet.OriginalRequestSignature, Query: false);
        var reply = await owner.transport.ExchangeReceiverIssueAsync(PartitionMovementClient.FirstVoter, selected, exchange,
            packet.OriginalRequestBytes.ToArray(), local, cancellationToken).ConfigureAwait(false);
        if (reply.Value.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Value.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        work.CheckResult(reply.Value);
    }

    internal static async Task<PartitionMoveReceiverIssuanceWitness> QueryReceiverIssueAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var remaining = expiry - owner.clock.GetUtcNow();
        var bounded = remaining < work.RemainingLifetime ? remaining : work.RemainingLifetime;
        if (bounded <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        using var deadline = new CancellationTokenSource(bounded, owner.clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = caller.Token;
        await owner.partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        _ = PartitionMovementClientReceiverIssueOperations.ReadCurrentReceiverIssueSource(owner, original, work);
        var selected = PartitionMovementClientReceiverIssueOperations.ReceiverIssueOwner(owner, original);
        var failures = new List<Exception>();
        for (var voter = PartitionMovementClient.FirstVoter; voter < selected.Owner.VoterIds.Length; voter++)
        {
            try
            {
                work.Check();
                token.ThrowIfCancellationRequested();
                return await PartitionMovementClientReceiverIssueQueryVoterOperations.QueryReceiverIssueVoterAsync(owner, original, selected, voter,
                    selected.Owner.VoterIds.Length - voter, expiry, work, token).ConfigureAwait(false);
            }
            catch (PartitionMovementOutcomeNetworkException failure)
            { failures.Add(failure.InnerException ?? failure); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    }

    internal static PartitionMoveParentState ReadCurrentReceiverIssueSource(PartitionMovementClient owner, PartitionMoveParentPhase original,
        ReadExecutionBudget work)
    {
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var grant = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var request = new PartitionMoveRequest(original.MoveId, original.Partition,
            phase.DestinationOwner.PhysicalShardId, phase.SourcePlacement.Revision, PartitionMoveMode.Resume);
        var read = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var current = owner.partition.Database.ReadPartitionMovementParentState(grant.OperatorPrincipalId,
            request, original.OriginalPhaseCommandId, work, read);
        work.CompleteReadGrant(read);
        var local = PhysicalOwnerConfiguredTuples.Local(owner.options.Value, owner.partition);
        if (current.Header is null || current.Pending is null || current.Pending.OriginalResult is not null
            || !PhysicalOwnerEntryValidation.SameOwner(local.Owner, current.Header.ControlOwner)
            || !NativeSerialization.Serialize(current.Pending).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return current;
    }

    internal static RegisteredPhysicalOwnerV1 ReceiverIssueOwner(PartitionMovementClient owner, PartitionMoveParentPhase original)
    {
        if (owner.options.Value.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !owner.options.Value.MembershipAuthority.RegisterPhysicalOwners || original.OriginalGrant is not { RequireReceiverIssuance: true })
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var control = PhysicalOwnerConfiguredTuples.Control(owner.options.Value, owner.partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(owner.options.Value, owner.partition);
        var selected = PhysicalOwnerEntryValidation.SameOwner(original.OriginalReceiverOwner, control.Owner) ? control : destination;
        if (!PhysicalOwnerEntryValidation.SameOwner(original.OriginalReceiverOwner, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        return selected;
    }

    internal static string SignReceiverIssueQuery(PartitionMovementClient owner, ReadOnlySpan<byte> bytes)
    {
        var key = Convert.FromBase64String(owner.options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.partition.Database.Limits.MaxBatchBytes);
            return mac.SignReceiverIssueRequest(bytes, query: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
