using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates configured A movement traffic before any receiver-local native capability is issued.</summary>
internal sealed class PartitionMovementPeerAdmission : IDisposable
{
    private const int EmptyBodyBytes = 0;
    private const string NonceFormat = "N";
    private const string SiloSeparator = ":";
    private const string InvalidProof = "The partition movement peer proof is invalid.";
    private readonly NodeOptions options;
    private readonly DatabaseEngine database;
    private readonly ReplicaConfiguration configuration;
    private readonly GrainRoutingOptions routing;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly PartitionMovementExecutionOptions execution;
    private readonly TimeProvider clock;
    private readonly ReplicaMembershipAuthorityAddressPins pins;
    private readonly ReplicaMembershipAuthorityReplayCache replay;

    internal PartitionMovementPeerAdmission(IOptions<NodeOptions> options, DatabaseEngine database, IOptions<ReplicaConfiguration> configuration,
        IOptions<GrainRoutingOptions> routing, IOptions<OrleansMembershipOptions> membership,
        IOptions<PartitionMovementExecutionOptions> execution, TimeProvider clock)
    {
        this.options = options.Value;
        this.database = database;
        this.configuration = configuration.Value;
        this.options.Validate();
        this.configuration.Validate();
        if (this.configuration.Incarnation != database.Store.Identity.Incarnation
            || this.configuration.LocalId != this.options.PublicEndpoint
            || !this.configuration.VoterIds.SequenceEqual(this.options.Peers, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        this.routing = routing.Value;
        this.membership = membership;
        this.execution = execution.Value;
        this.clock = clock;
        var control = PhysicalOwnerConfiguredTuples.Control(this.options, this.configuration);
        var endpoints = control.Endpoints.Select(value => new Uri(value).DnsSafeHost + SiloSeparator
            + MembershipAuthoritySettingsProtocol.NativeSiloPort.ToString(CultureInfo.InvariantCulture)).ToArray();
        pins = new(endpoints, membership);
        try
        { replay = new(clock, membership); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { pins.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task<PartitionMovementTransportRequest> VerifyAsync(ReadOnlyMemory<byte> originalBody,
        string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!execution.Enabled || !options.MembershipAuthority.RegisterPhysicalOwners)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidProof); }
        if (originalBody.Length <= EmptyBodyBytes || originalBody.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var ownedBody = originalBody.ToArray();
        using var mac = CreateControlMac();
        if (!mac.Verify(ownedBody, signature, reply: false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var request = NativeSerialization.Deserialize<PartitionMovementTransportRequest>(ownedBody);
        PartitionMovementTransportAdmission.Require(request);
        var envelope = request.Envelope;
        var maximum = database.Limits.MaxBatchBytes;
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, maximum);
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        var now = clock.GetUtcNow();
        if (request.CommandId == Guid.Empty || envelope.ExpiresAt <= now
            || envelope.ExpiresAt > now + routing.RequestLifetime
            || !PhysicalOwnerEntryValidation.SameOwner(envelope.ControlOwner, control.Owner)
            || !PartitionMovementConfiguredOwners.RequirePair(envelope, control.Owner, destination.Owner)
            || !control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(request.CallerSiloAddress,
                membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        if (!PartitionMoveGrantValidation.IsLocalControl(envelope.Stage))
        { PartitionMovementGrantProof.Require(request, control.Owner, local.Owner, now); }
        else if (!PhysicalOwnerEntryValidation.SameOwner(control.Owner, local.Owner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var address = SiloAddress.FromParsableString(request.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        await pins.PinCallerAsync(control.Owner.VoterIds.IndexOf(request.CallerVoter), address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
        if (!replay.TryUse(envelope.Nonce.ToString(NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return request;
    }

    private PartitionMovementMac CreateControlMac()
    {
        var configured = options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority
            ? options.PeerSecret : options.MembershipAuthority.AuthorityPeerSecret;
        var key = Convert.FromBase64String(configured
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof));
        try
        { return new(key, database.Limits.MaxBatchBytes); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        { pins.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { replay.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
