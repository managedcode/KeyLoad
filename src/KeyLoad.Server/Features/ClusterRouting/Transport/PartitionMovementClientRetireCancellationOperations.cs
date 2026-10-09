using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientRetireCancellationOperations
{
    internal static PartitionMoveRetireCancellationAttempt CreateRetireCancellationAttempt(PartitionMovementClient owner, PartitionMoveParentPhase original,
        Guid cancellationId, PartitionMoveReceiverSourceWitness freshSource, DateTimeOffset expiry, ReadExecutionBudget work)
    {
        work.Check();
        var current = PartitionMovementClientReceiverIssueOperations.ReadCurrentReceiverIssueSource(owner, original, work);
        if (original.Stage != PartitionMovePeerStage.Retire || original.OriginalExpiresAt > owner.clock.GetUtcNow()
            || original.RetireCancellationAttempt is not null || current.CurrentOperatorPolicyEpoch <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMovementProtocol.InvalidProof); }
        var envelope = PartitionMovementClientRetireCancellationOperations.RetireCancellationOriginalEnvelope(original);
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var request = new PartitionMovementRetireCancellationRequest(PartitionMoveProtocol.Version, cancellationId,
            original.OriginalPhaseCommandId, envelope, original.OriginalAuthorization!, original.CleanupGeneration,
            freshSource, Guid.NewGuid(), expiry, owner.partition.Configuration.LocalId, discovery.SiloAddress);
        var bytes = NativeSerialization.Serialize(request);
        if (bytes.Length > owner.partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        return new(request.Version, cancellationId, request.CancellationNonce, expiry, bytes,
            PartitionMovementClientRetireCancellationOperations.SignRetireCancellationRequest(owner, bytes, query: false));
    }

    internal static async Task SendRetireCancellationFirstAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        PartitionMoveRetireCancellationAttempt first, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        _ = PartitionMovementClientReceiverIssueOperations.ReadCurrentReceiverIssueSource(owner, original, work);
        var request = NativeSerialization.Deserialize<PartitionMovementRetireCancellationRequest>(first.OriginalRequestBytes.Span);
        if (first.CancellationExpiresAt <= owner.clock.GetUtcNow() || request.CancellationCommandId != first.CancellationCommandId
            || request.CancellationNonce != first.CancellationNonce || request.CancellationExpiresAt != first.CancellationExpiresAt
            || !NativeSerialization.Serialize(original.RetireCancellationAttempt).AsSpan().SequenceEqual(NativeSerialization.Serialize(first)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var selected = PartitionMovementClientReceiverIssueOperations.ReceiverIssueOwner(owner, original);
        var scope = PartitionMovementClientRetireCancellationOperations.RetireCancellationReplyScope(original, request.CancellationCommandId, request.CancellationNonce,
            request.CancellationExpiresAt, request.CallerVoter, request.CallerSiloAddress);
        var exchange = new PartitionMovementReceiverExchange(scope, original.OriginalPhaseIdentityDigest,
            first.OriginalRequestSignature, Query: false);
        var reply = await owner.transport.ExchangeRetireCancellationAsync(PartitionMovementClient.FirstVoter, selected, exchange,
            first.OriginalRequestBytes.ToArray(), PhysicalOwnerEntryValidation.SameOwner(selected.Owner, request.OriginalEnvelope.ControlOwner),
            cancellationToken).ConfigureAwait(false);
        if (reply.Value.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Value.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        work.CheckResult(reply.Value);
    }

    internal static async Task<PartitionMovementAuthenticatedReply> QueryRetireCancellationAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        Guid cancellationId, DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var selected = PartitionMovementClientReceiverIssueOperations.ReceiverIssueOwner(owner, original);
        var failures = new List<Exception>();
        for (var voter = PartitionMovementClient.FirstVoter; voter < selected.Owner.VoterIds.Length; voter++)
        {
            try
            {
                work.Check();
                cancellationToken.ThrowIfCancellationRequested();
                return await PartitionMovementClientRetireCancellationOperations.QueryRetireCancellationVoterAsync(owner, original, cancellationId, selected, voter,
                    selected.Owner.VoterIds.Length - voter, expiry, work, cancellationToken).ConfigureAwait(false);
            }
            catch (PartitionMovementOutcomeNetworkException failure) { failures.Add(failure.InnerException ?? failure); }
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

    internal static async Task<PartitionMovementAuthenticatedReply> QueryRetireCancellationVoterAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        Guid cancellationId, RegisteredPhysicalOwnerV1 selected, int voter, int remainingAttempts,
        DateTimeOffset expiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var envelope = PartitionMovementClientRetireCancellationOperations.RetireCancellationOriginalEnvelope(original);
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var maximumBytes = Math.Min(work.RemainingReadGrantBytes / remainingAttempts,
            Math.Min(owner.partition.Database.Limits.MaxQueryReadBytes, owner.partition.Database.Limits.MaxBatchBytes));
        var maximumRecords = Math.Min(work.RemainingReadGrantRecords / remainingAttempts, owner.partition.Database.Limits.MaxScanRecords);
        if (maximumBytes <= PartitionMoveProtocol.EmptyCount || maximumRecords <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var read = work.CreateReadGrant(maximumBytes, maximumRecords);
        var query = new PartitionMovementRetireCancellationQuery(PartitionMoveProtocol.Version, cancellationId,
            original.OriginalPhaseCommandId, envelope, original.OriginalAuthorization!, Guid.NewGuid(), expiry,
            owner.partition.Configuration.LocalId, discovery.SiloAddress, read.RemainingBytes, read.RemainingRecords,
            Math.Min(work.MaximumResultBytes, owner.partition.Database.Limits.MaxBatchBytes));
        var bytes = NativeSerialization.Serialize(query);
        if (bytes.Length > owner.partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var scope = PartitionMovementClientRetireCancellationOperations.RetireCancellationReplyScope(original, cancellationId, query.QueryNonce, expiry,
            query.CallerVoter, query.CallerSiloAddress);
        var exchange = new PartitionMovementReceiverExchange(scope, original.OriginalPhaseIdentityDigest,
            PartitionMovementClientRetireCancellationOperations.SignRetireCancellationRequest(owner, bytes, query: true), Query: true);
        var reply = await owner.transport.ExchangeRetireCancellationAsync(voter, selected, exchange, bytes,
            PhysicalOwnerEntryValidation.SameOwner(selected.Owner, envelope.ControlOwner), cancellationToken).ConfigureAwait(false);
        if (reply.Value.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Value.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value as PartitionMovementRetireCancellationOutcomeResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(read, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(read);
        work.CheckResult(actual);
        return reply;
    }

    internal static PartitionMovePeerEnvelope RetireCancellationOriginalEnvelope(PartitionMoveParentPhase original)
    {
        var envelope = original.OriginalReceiverIssuePacket is { } packet
            ? NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(packet.OriginalRequestBytes.Span).OriginalEnvelope
            : PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false);
        if (envelope.ReceiverIssuanceProof is not null || envelope.SourceDispatchWitness is not null
            || original.OriginalAuthorization is null || envelope.Stage != PartitionMovePeerStage.Retire
            || PartitionMoveOriginalDispatchIdentity.Digest(original.OriginalPhaseCommandId, envelope) != original.OriginalPhaseIdentityDigest)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        return envelope;
    }

    internal static PartitionMovementTransportRequest RetireCancellationReplyScope(PartitionMoveParentPhase original,
        Guid cancellationId, Guid nonce, DateTimeOffset expiry, string voter, string silo)
        => new(cancellationId, PartitionMovementClientRetireCancellationOperations.RetireCancellationOriginalEnvelope(original) with { Nonce = nonce, ExpiresAt = expiry },
            original.OriginalAuthorization, voter, silo, PartitionMovementTransportAction.Apply, Guid.Empty, PartitionMovementClient.FirstVoter);

    internal static string SignRetireCancellationRequest(PartitionMovementClient owner, ReadOnlySpan<byte> bytes, bool query)
    {
        var key = Convert.FromBase64String(owner.options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.partition.Database.Limits.MaxBatchBytes);
            return mac.SignRetireCancellationRequest(bytes, query);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
