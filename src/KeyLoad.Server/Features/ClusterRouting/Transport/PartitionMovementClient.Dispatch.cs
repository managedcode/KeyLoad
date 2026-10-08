using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementClient
{
    public async Task<PartitionMovementDispatchResult> DispatchAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireSenderAdmission();
        return await DispatchAdmittedAsync(phaseCommandId, original, authorization, action, handleId,
            ordinal, pinnedVoter, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMovementDispatchResult> DispatchAdmittedAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !options.MembershipAuthority.RegisterPhysicalOwners || original.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var receiver = original.Grant?.ReceiverOwner ?? original.ControlOwner;
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        var local = PhysicalOwnerEntryValidation.SameOwner(receiver, control.Owner);
        var selected = local ? control : destination;
        if (!PhysicalOwnerEntryValidation.SameOwner(receiver, selected.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var index = SelectVoter(selected, action, pinnedVoter);
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
        var admittedEnvelope = original with { Nonce = Guid.NewGuid() };
        var request = new PartitionMovementTransportRequest(phaseCommandId, admittedEnvelope, authorization,
            partition.Configuration.LocalId, discovery.SiloAddress,
            (PartitionMovementTransportAction)action, handleId, ordinal);
        PartitionMovementTransportAdmission.Require(request);
        var size = NativeSerialization.Measure(request);
        if (size < MinimumBodyBytes || size > partition.Database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var encoded = NativeSerialization.Serialize(request);
        var reply = await transport.ExchangeAsync(index, selected, request, encoded, local, cancellationToken).ConfigureAwait(false);
        return new(reply.Discovery.VoterId, reply.Reply);
    }

    private static int SelectVoter(RegisteredPhysicalOwnerV1 receiver, PartitionMovementPeerAction action,
        string? pinnedVoter)
    {
        if (action is PartitionMovementPeerAction.Page or PartitionMovementPeerAction.Release
            || action == PartitionMovementPeerAction.Capture && pinnedVoter is not null)
        {
            var index = receiver.Owner.VoterIds.IndexOf(pinnedVoter!);
            if (pinnedVoter is null || index < FirstVoter)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
            return index;
        }
        if (pinnedVoter is not null)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMovementProtocol.InvalidProof); }
        return FirstVoter;
    }
}
