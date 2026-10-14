using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementPeerAdmission
{
    /// <summary>Authenticates the current native caller independently of immutable historical signed packets.</summary>
    internal async Task<PartitionMovementGrainServiceRequest> VerifyNativeAsync(PartitionMovementGrainServiceRequest request,
        ReplicaSiloDiscovery local, Func<string, string, CancellationToken, Task> requireCaller,
        CancellationToken cancellationToken, Action<PartitionMovementNativeObservationStage>? observeStage = null)
    {
        observeStage?.Invoke(PartitionMovementNativeObservationStage.AdmissionShape);
        cancellationToken.ThrowIfCancellationRequested();
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var now = clock.GetUtcNow();
        if (!execution.Enabled || !options.PhysicalGroupTrust.RegisterPhysicalOwners
            || options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled
            || request is null || request.Payload.IsEmpty || request.Payload.Length > database.Limits.MaxBatchBytes
            || !Enum.IsDefined(request.Channel) || request.Nonce == Guid.Empty
            || request.ExpiresAt <= now || request.ExpiresAt > now + routing.RequestLifetime
            || request.Channel == PartitionMovementGrainServiceChannel.Phase && request.Query
            || (request.Channel is PartitionMovementGrainServiceChannel.Outcome or PartitionMovementGrainServiceChannel.TransferData) && !request.Query
            || request.Signature is null || request.Signature.Length != ReplicaTransportProtocol.HashHexCharacters
            || request.SenderSignature is null || request.SenderSignature.Length != ReplicaTransportProtocol.HashHexCharacters
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, membership)
            || request.DestinationVoter != local.VoterId || request.DestinationSiloAddress != local.SiloAddress
            || !local.TransportReady)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof);
        }
        request = request with
        {
            Payload = request.Payload.ToArray()
        };
        observeStage?.Invoke(PartitionMovementNativeObservationStage.OuterMac);
        using var mac = CreateControlMac();
        if (!mac.VerifyNativeRequest(request))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof);
        }
        observeStage?.Invoke(PartitionMovementNativeObservationStage.CallerDiscovery);
        await requireCaller(request.CallerVoter, request.CallerSiloAddress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return request;
    }

    internal void RequireNativeExpiryReplay(PartitionMovementGrainServiceRequest request,
        Action<PartitionMovementNativeObservationStage>? observeStage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observeStage?.Invoke(PartitionMovementNativeObservationStage.ExpiryReplay);
        if (request.ExpiresAt <= clock.GetUtcNow() || !nativeReplay.TryUse(request.Nonce.ToString(NonceFormat)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof);
        }
    }
}
