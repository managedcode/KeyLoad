using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class OwnerCapacityPeerAdmission(PartitionMovementPeerAdmission movement,
    IOptions<TenantPlacementExecutionOptions> execution) : IDisposable
{
    private const long EmptyGrant = 0;
    private const long MinimumPolicyEpoch = 1;
    private readonly PartitionMovementReplayCache replay = new(movement.Clock, movement.Membership);

    internal async Task VerifyAsync(OwnerCapacityNativeRequest request, ReplicaSiloDiscovery local,
        Func<string, string, CancellationToken, Task> requireCaller, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var control = PhysicalOwnerConfiguredTuples.Control(movement.Options, movement.Configuration);
        var actual = PhysicalOwnerConfiguredTuples.Local(movement.Options, movement.Configuration);
        if (!execution.Value.Enabled || !movement.Execution.Enabled || !movement.Options.PhysicalGroupTrust.RegisterPhysicalOwners
            || request is null || request.Version != TenantPlacementPolicyProtocol.Version
            || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty
            || string.IsNullOrWhiteSpace(request.PrincipalId) || request.PolicyEpoch < MinimumPolicyEpoch
            || request.MaximumReadBytes <= EmptyGrant || request.MaximumExaminedRecords <= EmptyGrant || request.MaximumResultBytes <= EmptyGrant
            || request.Partitions.IsDefaultOrEmpty || request.Partitions.Length > execution.Value.MaximumPlanPartitions
            || request.Partitions.Distinct().Count() != request.Partitions.Length
            || request.Signature is null || request.Signature.Length != ReplicaTransportProtocol.HashHexCharacters
            || !PhysicalOwnerEntryValidation.Valid(request.Control)
            || !PhysicalOwnerEntryValidation.Valid(request.Destination)
            || !PhysicalOwnerEntryValidation.Same(request.Control, control)
            || !PhysicalOwnerEntryValidation.Same(request.Destination, actual)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, movement.Membership)
            || request.DestinationVoter != local.VoterId || request.DestinationSiloAddress != local.SiloAddress
            || request.Destination.Owner.Incarnation != local.Incarnation || !local.TransportReady
            || string.IsNullOrWhiteSpace(request.CohortFingerprint)
            || NativeSerialization.Measure(request) > movement.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
        }
        using var mac = movement.CreateControlMac();
        if (!OwnerCapacityMac.VerifyCapacityRequest(mac, request))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
        }
        await requireCaller(request.CallerVoter, request.CallerSiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    internal void RequireExpiryReplay(OwnerCapacityNativeRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = movement.Clock.GetUtcNow();
        if (request.IssuedAt > now || request.ExpiresAt <= now
            || request.ExpiresAt > request.IssuedAt + movement.Routing.RequestLifetime
            || !replay.TryUse(request.Nonce.ToString(PartitionMovementPeerAdmission.NonceFormat)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
        }
    }

    public void Dispose() => replay.Dispose();
}
