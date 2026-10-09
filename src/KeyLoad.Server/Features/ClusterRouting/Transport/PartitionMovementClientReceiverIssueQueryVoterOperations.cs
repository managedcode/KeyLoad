using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientReceiverIssueQueryVoterOperations
{
    internal static async Task<PartitionMoveReceiverIssuanceWitness> QueryReceiverIssueVoterAsync(PartitionMovementClient owner, PartitionMoveParentPhase original,
        RegisteredPhysicalOwnerV1 selected, int voter, int remainingAttempts, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var authorization = original.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false);
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var maximumBytes = Math.Min(work.RemainingReadGrantBytes / remainingAttempts,
            Math.Min(owner.partition.Database.Limits.MaxQueryReadBytes, owner.partition.Database.Limits.MaxBatchBytes));
        var maximumRecords = Math.Min(work.RemainingReadGrantRecords / remainingAttempts, owner.partition.Database.Limits.MaxScanRecords);
        if (maximumBytes <= PartitionMovementProtocol.NoReadBytes || maximumRecords <= PartitionMovementProtocol.NoExaminedRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var read = work.CreateReadGrant(maximumBytes, maximumRecords);
        var query = new PartitionMovementReceiverIssueQuery(PartitionMoveProtocol.Version,
            original.OriginalPhaseCommandId, envelope, authorization, Guid.NewGuid(), expiry,
            owner.partition.Configuration.LocalId, discovery.SiloAddress, read.RemainingBytes, read.RemainingRecords,
            Math.Min(work.MaximumResultBytes, owner.partition.Database.Limits.MaxBatchBytes));
        var bytes = NativeSerialization.Serialize(query);
        if (bytes.Length > owner.partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var scope = new PartitionMovementTransportRequest(original.OriginalPhaseCommandId,
            envelope with { Nonce = query.QueryNonce, ExpiresAt = expiry }, authorization,
            query.CallerVoter, query.CallerSiloAddress, PartitionMovementTransportAction.Apply, Guid.Empty, PartitionMovementClient.FirstVoter);
        var exchange = new PartitionMovementReceiverExchange(scope, original.OriginalPhaseIdentityDigest,
            PartitionMovementClientReceiverIssueOperations.SignReceiverIssueQuery(owner, bytes), Query: true);
        var local = PhysicalOwnerEntryValidation.SameOwner(selected.Owner, envelope.ControlOwner);
        var reply = await owner.transport.ExchangeReceiverIssueAsync(voter, selected, exchange,
            bytes, local, cancellationToken).ConfigureAwait(false);
        if (reply.Value.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Value.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value as PartitionMovementReceiverIssuanceResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(read, actual.ReadBytes, actual.ExaminedRecords);
        work.CompleteReadGrant(read);
        var witness = new PartitionMoveReceiverIssuanceWitness(PartitionMoveProtocol.Version,
            original.OriginalPhaseCommandId, query.QueryNonce, reply.OriginalBytes, reply.Signature);
        work.CheckResult(witness);
        return witness;
    }
}
