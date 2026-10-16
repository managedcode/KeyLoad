using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates inner movement frames and validates their original configured-owner scope.</summary>
internal static class PartitionMovementPeerFrameAdmission
{
    internal static Task<PartitionMovementTransportRequest> VerifyAsync(PartitionMovementPeerAdmission admission,
        ReadOnlyMemory<byte> originalBody,
        string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!admission.Execution.Enabled || !admission.Options.PhysicalGroupTrust.RegisterPhysicalOwners
            || admission.Options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof);
        }
        if (originalBody.Length <= PartitionMovementPeerAdmission.EmptyBodyBytes || originalBody.Length > admission.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var ownedBody = originalBody.ToArray();
        using var mac = admission.CreateControlMac();
        if (!mac.Verify(ownedBody, signature, reply: false))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var request = NativeSerialization.Deserialize<PartitionMovementTransportRequest>(ownedBody);
        if (request.Envelope.Stage == PartitionMovePeerStage.ControlApplyCommand)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        return Task.FromResult(ValidateVerifiedRequest(admission, request, cancellationToken));
    }

    private static PartitionMovementTransportRequest ValidateVerifiedRequest(PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PartitionMovementTransportAdmission.Require(request);
        var envelope = request.Envelope;
        var maximum = admission.Database.Limits.MaxBatchBytes;
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, maximum);
        var control = PhysicalOwnerConfiguredTuples.Control(admission.Options, admission.Configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(admission.Options, admission.Configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(admission.Options, admission.Configuration);
        var now = admission.Clock.GetUtcNow();
        if (request.CommandId == Guid.Empty || envelope.ExpiresAt <= now
            || envelope.ExpiresAt > now + admission.Routing.RequestLifetime
            || !PhysicalOwnerEntryValidation.SameOwner(envelope.ControlOwner, control.Owner)
            || !PartitionMovementConfiguredOwners.RequirePair(envelope, control.Owner, destination.Owner)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, admission.Membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        if (!PartitionMoveGrantValidation.IsLocalControl(envelope.Stage))
        {
            PartitionMovementGrantProof.Require(request, control.Owner, local.Owner, now);
        }
        else if (!PhysicalOwnerEntryValidation.SameOwner(control.Owner, local.Owner))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        if (!admission.Replay.TryUse(envelope.Nonce.ToString(PartitionMovementPeerAdmission.NonceFormat)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        admission.Database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return request;
    }

    /// <summary>Authenticates the new carrier before sharing the original configured-owner admission path.</summary>
    internal static Task<(PartitionMovementTransportRequest Request, PartitionMoveLifetimeProofV1 Proof)>
        VerifyControlledLifetimeAsync(PartitionMovementPeerAdmission admission, ReadOnlyMemory<byte> originalBody, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!admission.Execution.Enabled || !admission.Options.PhysicalGroupTrust.RegisterPhysicalOwners
            || admission.Options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementPeerAdmission.InvalidProof);
        }
        if (originalBody.Length <= PartitionMovementPeerAdmission.EmptyBodyBytes || originalBody.Length > admission.Database.Limits.MaxBatchBytes
            || signature is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var ownedBody = originalBody.ToArray();
        using var mac = admission.CreateControlMac();
        if (!mac.VerifyControlledLifetime(ownedBody, signature))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var carrier = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV1>(ownedBody);
        if (carrier.OriginalRequest.IsEmpty || carrier.Proof is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        var request = NativeSerialization.Deserialize<PartitionMovementTransportRequest>(carrier.OriginalRequest.Span);
        if (request.Action != PartitionMovementTransportAction.Apply
            || request.Envelope.Stage != PartitionMovePeerStage.ControlApplyCommand)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementPeerAdmission.InvalidProof);
        }
        request = ValidateVerifiedRequest(admission, request, cancellationToken);
        return Task.FromResult((Request: request, Proof: carrier.Proof));
    }
}
