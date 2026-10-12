using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates configured A movement traffic before any receiver-local native capability is issued.</summary>
internal sealed partial class PartitionMovementPeerAdmission : IDisposable
{
    internal const int EmptyBodyBytes = 0;
    internal const string NonceFormat = "N";
    internal const string InvalidProof = "The partition movement peer proof is invalid.";
    private readonly NodeOptions options;
    private readonly DatabaseEngine database;
    private readonly ReplicaConfiguration configuration;
    private readonly GrainRoutingOptions routing;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly PartitionMovementExecutionOptions execution;
    private readonly TimeProvider clock;
    private readonly PartitionMovementReplayCache replay;
    private readonly PartitionMovementReplayCache nativeReplay;

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
        replay = new(clock, membership);
        try
        { nativeReplay = new(clock, membership); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { replay.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<PartitionMovementTransportRequest> VerifyAsync(ReadOnlyMemory<byte> originalBody,
        string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!execution.Enabled || !options.PhysicalGroupTrust.RegisterPhysicalOwners
            || options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
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
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        if (!PartitionMoveGrantValidation.IsLocalControl(envelope.Stage))
        { PartitionMovementGrantProof.Require(request, control.Owner, local.Owner, now); }
        else if (!PhysicalOwnerEntryValidation.SameOwner(control.Owner, local.Owner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        if (!replay.TryUse(envelope.Nonce.ToString(NonceFormat)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        database.VerifyPhysicalCommandOwner(local.Owner, false, cancellationToken);
        return Task.FromResult(request);
    }

    internal PartitionMovementMac CreateControlMac()
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        if (!PhysicalOwnerEntryValidation.Valid(control) || !PhysicalOwnerEntryValidation.Valid(destination)
            || !PhysicalOwnerEntryValidation.Valid(local))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        var configured = options.PhysicalGroupTrust.Role switch
        {
            PhysicalGroupTrustRole.Control when PhysicalOwnerEntryValidation.SameOwner(local.Owner, control.Owner)
                && !PhysicalOwnerEntryValidation.SameOwner(local.Owner, destination.Owner) => options.PeerSecret,
            PhysicalGroupTrustRole.Destination when PhysicalOwnerEntryValidation.SameOwner(local.Owner, destination.Owner)
                && !PhysicalOwnerEntryValidation.SameOwner(local.Owner, control.Owner) => options.PhysicalGroupTrust.ForeignPeerSecret,
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof)
        };
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
        { nativeReplay.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { replay.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal PartitionMovementExecutionOptions Execution => execution;
    internal NodeOptions Options => options;
    internal DatabaseEngine Database => database;
    internal ReplicaConfiguration Configuration => configuration;
    internal TimeProvider Clock => clock;
    internal GrainRoutingOptions Routing => routing;
    internal IOptions<OrleansMembershipOptions> Membership => membership;
    internal PartitionMovementReplayCache Replay => replay;

    internal Task<PartitionMovementOutcomeTransportRequest> VerifyOutcomeAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementOutcomePeerAdmission.VerifyOutcomeAsync(this, body, signature, token);

    internal Task<PartitionMovementTransferDataRequest> VerifyTransferDataAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementTransferDataPeerAdmission.VerifyTransferDataAsync(this, body, signature, token);

    internal Task<PartitionMovementReceiverIssueRequest> VerifyReceiverIssueAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementReceiverIssuePeerAdmission.VerifyReceiverIssueAsync(this, body, signature, token);

    internal Task<PartitionMovementReceiverIssueQuery> VerifyReceiverIssueQueryAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementReceiverIssuePeerAdmission.VerifyReceiverIssueQueryAsync(this, body, signature, token);

    internal Task<PartitionMovementRetireCancellationRequest> VerifyRetireCancellationAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementRetireCancellationPeerAdmission.VerifyRetireCancellationAsync(this, body, signature, token);

    internal Task<PartitionMovementRetireCancellationQuery> VerifyRetireCancellationQueryAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken token)
        => PartitionMovementRetireCancellationPeerAdmission.VerifyRetireCancellationQueryAsync(this, body, signature, token);
}
