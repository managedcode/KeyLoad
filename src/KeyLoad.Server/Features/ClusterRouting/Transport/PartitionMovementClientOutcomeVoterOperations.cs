using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientOutcomeVoterOperations
{
    internal static async Task<PartitionMovementOutcomeWitness> QueryOutcomeVoterAsync(PartitionMovementClient owner, Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        RegisteredPhysicalOwnerV1 selected, int voter, int remainingAttempts, bool local,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var limits = owner.partition.Database.Limits;
        var maximumBytes = Math.Min(work.RemainingReadGrantBytes / remainingAttempts,
            Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes));
        var maximumRecords = Math.Min(work.RemainingReadGrantRecords / remainingAttempts,
            Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords));
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var discovery = owner.node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var identity = new PartitionMovementTransportRequest(phaseCommandId, original, authorization,
            owner.partition.Configuration.LocalId, discovery.SiloAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, PartitionMovementClient.FirstVoter);
        var query = new PartitionMovementOutcomeTransportRequest(identity, requestExpiry, Guid.NewGuid(),
            maximumBytes, maximumRecords, work.MaximumResultBytes);
        var size = NativeSerialization.Measure(query);
        if (size < PartitionMovementClient.MinimumBodyBytes || size > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var body = NativeSerialization.Serialize(query);
        var reply = await owner.transport.ExchangeOutcomeAsync(voter, selected, query, body, local,
            cancellationToken).ConfigureAwait(false);
        work.Check();
        if (reply.Value.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Value.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var witness = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value as PartitionMovementOutcomeWitness
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        witness = witness with
        {
            TransportProof = new(PartitionMoveProtocol.Version,
            PartitionMoveOutcomeProofPurpose.OutcomeReadReply, phaseCommandId, query.Nonce,
            reply.OriginalBytes, reply.Signature)
        };
        work.MeasureResult(witness);
        work.ImportReadGrant(grant, witness.ReadBytes, witness.ExaminedRecords);
        work.CompleteReadGrant(grant);
        work.Check();
        return witness;
    }
}
