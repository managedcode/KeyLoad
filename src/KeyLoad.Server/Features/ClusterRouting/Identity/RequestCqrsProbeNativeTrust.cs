using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Selects a configured group credential only after an exact physical tuple match.</summary>
internal sealed class RequestCqrsProbeNativeTrust : IDisposable
{
    private readonly List<(PhysicalShardRecord Owner, PartitionMovementMac Mac)> groups = [];
    private readonly ReplicaNativeDiscoveryMembership membership;
    private readonly IOptions<OrleansMembershipOptions> bounds;
    internal PhysicalShardRecord Local { get; }
    internal string Voter { get; }
    internal string Address { get; }
    internal ImmutableArray<PhysicalShardRecord> Owners => groups.Select(group => group.Owner).ToImmutableArray();

    internal RequestCqrsProbeNativeTrust(IOptions<NodeOptions> nodeOptions, IOptions<ReplicaConfiguration> configurationOptions, ILocalSiloDetails local,
        ISiloStatusOracle oracle, IOptions<OrleansMembershipOptions> bounds)
    {
        ArgumentNullException.ThrowIfNull(nodeOptions);
        ArgumentNullException.ThrowIfNull(nodeOptions.Value);
        ArgumentNullException.ThrowIfNull(configurationOptions);
        ArgumentNullException.ThrowIfNull(configurationOptions.Value);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(bounds.Value);
        var node = nodeOptions.Value;
        var configuration = configurationOptions.Value;
        this.bounds = bounds;
        membership = new(oracle, bounds);
        Local = PhysicalOwnerConfiguredTuples.Local(node, configuration).Owner;
        Voter = configuration.LocalId;
        Address = local.SiloAddress.ToParsableString();
        if (!node.RequestCqrsProbe.Enabled || Local.VoterIds.Length != RequestCqrsProbeNativeProtocol.FixedMembers
            || !Local.VoterIds.Contains(Voter, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RequestCqrsProbeNativeProtocol.Unavailable); }
        try
        {
            Add(Local, node.PeerSecret);
            if (node.PhysicalGroupTrust.Role is PhysicalGroupTrustRole.Control or PhysicalGroupTrustRole.Destination)
            {
                var foreign = node.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Control
                    ? PhysicalOwnerConfiguredTuples.Destination(node, configuration).Owner
                    : PhysicalOwnerConfiguredTuples.Control(node, configuration).Owner;
                if (foreign.VoterIds.Length != RequestCqrsProbeNativeProtocol.FixedMembers
                    || foreign.VoterIds.Intersect(Local.VoterIds, StringComparer.Ordinal).Any()
                    || foreign.PhysicalShardId == Local.PhysicalShardId || foreign.Incarnation == Local.Incarnation)
                { throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable); }
                Add(foreign, node.PhysicalGroupTrust.ForeignPeerSecret
                    ?? throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable));
            }
            else if (node.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Disabled)
            { throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable); }
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal PartitionMovementMac Mac(PhysicalShardRecord owner)
    {
        var match = groups.SingleOrDefault(group => Same(group.Owner, owner));
        return match.Mac ?? throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable);
    }

    internal SiloAddress RequireCurrent(PhysicalShardRecord owner, string voter, string address)
    {
        _ = Mac(owner);
        if (!owner.VoterIds.Contains(voter, StringComparer.Ordinal) || !PhysicalGroupAddress.CanonicalAddress(address, bounds))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable); }
        var actual = SiloAddress.FromParsableString(address);
        if (!membership.IsCurrent(voter, actual))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RequestCqrsProbeNativeProtocol.Unavailable); }
        return actual;
    }

    internal SiloAddress Resolve(PhysicalShardRecord owner, string voter)
    {
        _ = Mac(owner);
        if (!owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RequestCqrsProbeNativeProtocol.Unavailable); }
        return membership.Resolve(voter)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RequestCqrsProbeNativeProtocol.Unavailable);
    }

    internal static bool Same(PhysicalShardRecord left, PhysicalShardRecord? right)
        => right is not null && right.PlacementEpoch == left.PlacementEpoch
            && PhysicalOwnerEntryValidation.SameOwner(left, right);

    private void Add(PhysicalShardRecord owner, string secret)
    {
        var key = Convert.FromBase64String(secret);
        try { groups.Add((owner, new(key, bounds.Value.MaximumRequestBytes))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var group in groups)
        { ServerFailureObserver.Observe(group.Mac.Dispose, failures); }
        groups.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
