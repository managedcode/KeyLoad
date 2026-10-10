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
        PhysicalOwnerProbeTrace trace, CancellationToken cancellationToken)
    {
        trace.Stage = PhysicalOwnerProbeStage.CallValidation;
        Validate(call, cancellationToken);
        trace.Stage = PhysicalOwnerProbeStage.InitialMembership;
        var initial = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        trace.Stage = PhysicalOwnerProbeStage.Authentication;
        var administrator = await requests.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        trace.Stage = PhysicalOwnerProbeStage.NativeStatus;
        var status = await requests.ReadStatusAsync(administrator, cancellationToken).ConfigureAwait(false);
        trace.Stage = PhysicalOwnerProbeStage.StatusGuard;
        if (trace.Refused(PhysicalOwnerProbePredicate.StatusIncarnation, status.Incarnation != partition.Configuration.Incarnation)
            || trace.Refused(PhysicalOwnerProbePredicate.StatusVoters, status.Voters != PhysicalOwnerDirectoryProtocol.VoterCount) || trace.Refused(PhysicalOwnerProbePredicate.StatusRoutingReady, !status.RoutingReady))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        trace.Stage = PhysicalOwnerProbeStage.ReadBarrier;
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        trace.Stage = PhysicalOwnerProbeStage.CallValidation;
        Validate(call, cancellationToken);
        trace.Stage = PhysicalOwnerProbeStage.Consensus;
        var local = await partition.Consensus.StateAsync(cancellationToken).ConfigureAwait(false);
        trace.Stage = PhysicalOwnerProbeStage.Discovery;
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        trace.Stage = PhysicalOwnerProbeStage.FinalMembership;
        var final = await node.MembershipReadyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        trace.Stage = PhysicalOwnerProbeStage.CallValidation;
        Validate(call, cancellationToken);
        trace.Stage = PhysicalOwnerProbeStage.FinalGuard;
        if (trace.Refused(PhysicalOwnerProbePredicate.Fingerprint, initial.ActiveFingerprint != final.ActiveFingerprint) || trace.Refused(PhysicalOwnerProbePredicate.TransportReady, !discovery.TransportReady)
            || trace.Refused(PhysicalOwnerProbePredicate.LocalVoter, discovery.VoterId != partition.Configuration.LocalId)
            || trace.Refused(PhysicalOwnerProbePredicate.DiscoveryIncarnation, discovery.Incarnation != partition.Configuration.Incarnation)
            || trace.Refused(PhysicalOwnerProbePredicate.AppliedPosition, local.MaterializedPosition < EmptyApplied))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        trace.Stage = PhysicalOwnerProbeStage.CurrentAdministrator;
        partition.Database.Store.Read(view =>
        {
            GrainRequestAuthority.RequireAdministrator(partition.Database.Principal(view, administrator.Id, clock.GetUtcNow()));
            return true;
        });
        trace.Stage = PhysicalOwnerProbeStage.Reply;
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
