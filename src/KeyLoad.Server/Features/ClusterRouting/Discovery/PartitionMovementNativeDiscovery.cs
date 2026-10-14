using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Finds configured voters only through actual bounded active native silo generations.</summary>
internal sealed class PartitionMovementNativeDiscovery(PartitionHost partition, IOptions<NodeOptions> options,
    IOptions<OrleansMembershipOptions> membership, ISiloStatusOracle oracle,
    Func<SiloAddress, Guid, CancellationToken, Task<PartitionMovementGrainServiceDiscovery>> discover)
{
    private const int FirstVoter = 0;
    private readonly NodeOptions settings = options.Value;

    internal async Task<SiloAddress> ResolveAsync(RegisteredPhysicalOwnerV1 owner, int index, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (index < FirstVoter || index >= owner.Owner.VoterIds.Length
            || !PhysicalOwnerEntryValidation.SameOwner(owner.Owner, RequireDestination(owner.Owner.VoterIds[index]).Owner))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        var active = Active();
        SiloAddress? found = null;
        var failures = new List<Exception>();
        foreach (var address in active.Keys.OrderBy(value => value.ToParsableString(), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var nonce = Guid.NewGuid();
            PartitionMovementGrainServiceDiscovery reply;
            try
            {
                reply = await discover(address, nonce, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OrleansException or TimeoutException)
            {
                failures.Add(error);
                continue;
            }
            var identity = PartitionMovementNativeDiscoveryIdentity.ReadOwned(reply);
            // These untrusted fields only reject an unrelated candidate; they never authorize a target.
            if (identity.VoterId != owner.Owner.VoterIds[index])
            {
                continue;
            }
            PartitionMovementNativeDiscoveryIdentity.Require(settings, partition.Configuration, owner,
                address, nonce, reply, identity, membership);
            if (found is not null)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
            }
            found = address;
        }
        token.ThrowIfCancellationRequested();
        if (found is null)
        {
            throw new PartitionMovementNativeRouteUnavailableException(failures);
        }
        if (oracle.GetApproximateSiloStatus(found) != SiloStatus.Active)
        {
            throw new PartitionMovementNativeRouteUnavailableException();
        }
        return found;
    }

    internal async Task RequireCallerAsync(string voter, string address, CancellationToken token,
        Action<PartitionMovementNativeObservationStage>? observeStage = null)
    {
        observeStage?.Invoke(PartitionMovementNativeObservationStage.CallerIdentity);
        token.ThrowIfCancellationRequested();
        var control = PhysicalOwnerConfiguredTuples.Control(settings, partition.Configuration);
        if (!control.Owner.VoterIds.Contains(voter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(address, membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        var actual = SiloAddress.FromParsableString(address);
        observeStage?.Invoke(PartitionMovementNativeObservationStage.CallerActive);
        if (!Active().ContainsKey(actual))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        var nonce = Guid.NewGuid();
        observeStage?.Invoke(PartitionMovementNativeObservationStage.CallerDiscovery);
        var reply = await discover(actual, nonce, token).ConfigureAwait(false);
        observeStage?.Invoke(PartitionMovementNativeObservationStage.DiscoveryShape);
        var identity = PartitionMovementNativeDiscoveryIdentity.ReadOwned(reply);
        observeStage?.Invoke(PartitionMovementNativeObservationStage.DiscoveryVoter);
        if (identity.VoterId != voter)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        PartitionMovementNativeDiscoveryIdentity.Require(settings, partition.Configuration, control,
            actual, nonce, reply, identity, membership, observeStage);
        token.ThrowIfCancellationRequested();
        observeStage?.Invoke(PartitionMovementNativeObservationStage.CallerActiveAfterDiscovery);
        if (oracle.GetApproximateSiloStatus(actual) != SiloStatus.Active)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
    }

    internal RegisteredPhysicalOwnerV1 RequireDestination(string voter)
    {
        var control = PhysicalOwnerConfiguredTuples.Control(settings, partition.Configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(settings, partition.Configuration);
        if (control.Owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            return control;
        }
        if (destination.Owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            return destination;
        }
        throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
    }

    private Dictionary<SiloAddress, SiloStatus> Active()
    {
        var active = oracle.GetApproximateSiloStatuses(onlyActive: true);
        if (active.Count > membership.Value.MaximumRows)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable);
        }
        return active;
    }
}
