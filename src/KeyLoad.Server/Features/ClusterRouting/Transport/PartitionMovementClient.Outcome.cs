using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementClient
{
    public async Task<PartitionMovementOutcomeWitness> QueryOutcomeAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization, string originalVoter,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !options.MembershipAuthority.RegisterPhysicalOwners || requestExpiry <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        work.Check();
        partition.Database.ValidatePartitionMovementOutcomeQuery(original, authorization, phaseCommandId, work);
        work.Check();
        var receiver = original.Grant?.ReceiverOwner ?? original.ControlOwner;
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        var local = PhysicalOwnerEntryValidation.SameOwner(receiver, control.Owner);
        var selected = local ? control : destination;
        var index = selected.Owner.VoterIds.IndexOf(originalVoter);
        if (index < FirstVoter || !PhysicalOwnerEntryValidation.SameOwner(receiver, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var limits = partition.Database.Limits;
        var maximumBytes = Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes);
        var maximumRecords = Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var identity = new PartitionMovementTransportRequest(phaseCommandId, original, authorization,
            partition.Configuration.LocalId, discovery.SiloAddress, PartitionMovementTransportAction.Apply,
            Guid.Empty, FirstVoter);
        var query = new PartitionMovementOutcomeTransportRequest(identity, requestExpiry, Guid.NewGuid(),
            maximumBytes, maximumRecords, work.MaximumResultBytes);
        var size = NativeSerialization.Measure(query);
        if (size < MinimumBodyBytes || size > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var body = NativeSerialization.Serialize(query);
        var reply = await transport.ExchangeOutcomeAsync(index, selected, query, body, local, cancellationToken).ConfigureAwait(false);
        if (reply.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var witness = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementOutcomeWitness
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        work.ImportReadGrant(grant, witness.ReadBytes, witness.ExaminedRecords);
        work.MeasureResult(witness);
        work.Check();
        return witness;
    }
}
