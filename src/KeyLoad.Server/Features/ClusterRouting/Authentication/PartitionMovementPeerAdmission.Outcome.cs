using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementPeerAdmission
{
    internal async Task<PartitionMovementOutcomeTransportRequest> VerifyOutcomeAsync(
        ReadOnlyMemory<byte> originalBody, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!execution.Enabled || !options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidProof); }
        if (originalBody.Length <= EmptyBodyBytes || originalBody.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var ownedBody = originalBody.ToArray();
        using var mac = CreateControlMac();
        if (!mac.VerifyOutcome(ownedBody, signature, reply: false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var query = NativeSerialization.Deserialize<PartitionMovementOutcomeTransportRequest>(ownedBody);
        var now = clock.GetUtcNow();
        PartitionMovementOutcomeAdmission.Require(query, database.Limits, now, routing.RequestLifetime);
        var request = query.Original;
        var envelope = request.Envelope;
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, database.Limits.MaxBatchBytes);
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        if (!PhysicalOwnerEntryValidation.SameOwner(envelope.ControlOwner, control.Owner)
            || !PartitionMovementConfiguredOwners.RequirePair(envelope, control.Owner, destination.Owner)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(request.CallerSiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        RequireOutcomeJournal(request, control.Owner, local.Owner);
        var address = SiloAddress.FromParsableString(request.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        await pins.PinCallerAsync(control.Owner.VoterIds.IndexOf(request.CallerVoter), address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
        if (!replay.TryUse(query.Nonce.ToString(NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return query;
    }

    private static void RequireOutcomeJournal(PartitionMovementTransportRequest request,
        PhysicalShardRecord control, PhysicalShardRecord local)
    {
        if (PartitionMoveGrantValidation.IsLocalControl(request.Envelope.Stage))
        {
            if (request.Envelope.Grant is not null || request.ControlGrantJournal is not null
                || !PhysicalOwnerEntryValidation.SameOwner(control, local))
            { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
            return;
        }
        var grant = request.Envelope.Grant;
        var journal = request.ControlGrantJournal;
        if (grant is null || journal is null || grant.PhaseCommandId != request.CommandId
            || journal.CommandId != grant.GrantId || journal.AppliedPosition != grant.AdmissionPosition
            || journal.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(journal.PhysicalOwner, control)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, local))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
    }
}
