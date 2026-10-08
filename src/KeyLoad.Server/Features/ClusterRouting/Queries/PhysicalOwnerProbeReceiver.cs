using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeReceiver(OrleansNode node, PartitionHost partition,
    PhysicalOwnerStartupRequests requests, IOptions<NodeOptions> nodeOptions,
    IOptions<GrainRoutingOptions> routingOptions, IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock)
{
    private const long EmptyApplied = 0;

    internal async Task<PhysicalOwnerProbeReplyV1> ReadAsync(PhysicalOwnerProbeCallV1 call,
        CancellationToken cancellationToken)
    {
        Validate(call, cancellationToken);
        var initial = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        var administrator = await requests.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        var status = await requests.ReadStatusAsync(administrator, cancellationToken).ConfigureAwait(false);
        if (status.Incarnation != partition.Configuration.Incarnation
            || status.Voters != PhysicalOwnerDirectoryProtocol.VoterCount || !status.RoutingReady)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        Validate(call, cancellationToken);
        var local = await partition.Consensus.StateAsync(cancellationToken).ConfigureAwait(false);
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        var final = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        Validate(call, cancellationToken);
        if (initial.ActiveFingerprint != final.ActiveFingerprint || !discovery.TransportReady
            || discovery.VoterId != partition.Configuration.LocalId
            || discovery.Incarnation != partition.Configuration.Incarnation
            || local.MaterializedPosition < EmptyApplied)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        partition.Database.Store.Read(view =>
        {
            GrainRequestAuthority.RequireAdministrator(partition.Database.Principal(view, administrator.Id, clock.GetUtcNow()));
            return true;
        });
        return new(PhysicalOwnerProbeProtocol.Version, call.RequestId, call.Nonce,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition), discovery,
            local.MaterializedPosition, final.ActiveFingerprint);
    }

    internal void Validate(PhysicalOwnerProbeCallV1 call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        var options = nodeOptions.Value;
        var now = clock.GetUtcNow();
        if (!options.MembershipAuthority.RegisterPhysicalOwners
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Proxy
            || call.Version != PhysicalOwnerProbeProtocol.Version || call.RequestId == Guid.Empty
            || !ReplicaMembershipAuthorityValidation.ValidNonce(call.Nonce)
            || !options.MembershipAuthority.AuthorityEndpoints.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(call.CallerSiloAddress, membershipOptions)
            || !PhysicalOwnerEntryValidation.Valid(call.Control) || !PhysicalOwnerEntryValidation.Valid(call.Destination)
            || call.ExpiresAt <= now || call.IssuedAt - now > membershipOptions.Value.ClockSkew || call.ExpiresAt <= call.IssuedAt
            || call.ExpiresAt - call.IssuedAt > routingOptions.Value.ExecutionLifetime
            || !PhysicalOwnerEntryValidation.Same(call.Control, PhysicalOwnerConfiguredTuples.Control(options, partition))
            || !PhysicalOwnerEntryValidation.Same(call.Destination, PhysicalOwnerConfiguredTuples.Local(options, partition)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
    }
}
