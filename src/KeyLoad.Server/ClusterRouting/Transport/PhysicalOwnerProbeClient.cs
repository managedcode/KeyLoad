using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeClient(IOptions<NodeOptions> nodeOptions, OrleansNode node,
    PartitionHost partition, IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock) : IDisposable
{
    private const int FirstVoter = 0;
    private const int ActiveResource = 0;
    private const int DisposedResource = 1;
    private const int EmptyErrorSignatureLength = 0;
    private const long EmptyApplied = 0;
    private readonly NodeOptions options = nodeOptions.Value;
    private int disposed;
    internal ErrorCode? LastNativeError
    {
        get; private set;
    }

    internal async Task<string> VerifyAllAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != ActiveResource, this);
        LastNativeError = null;
        try
        {
            if (!options.PhysicalGroupTrust.RegisterPhysicalOwners
                || options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable);
            }
            var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
            var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
            var discovery = node.Discovery?.Read()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
            string? fingerprint = null;
            for (var index = FirstVoter; index < destination.Owner.VoterIds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = new PhysicalOwnerProbeCallV1(PhysicalOwnerProbeProtocol.Version, Guid.NewGuid(),
                    control, destination, partition.Configuration.LocalId, discovery.SiloAddress,
                    WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(PhysicalOwnerProbeNativeIdentity.NonceBytes)),
                    clock.GetUtcNow(), expiresAt);
                var observed = await ReadAsync(index, call, cancellationToken).ConfigureAwait(false);
                if (fingerprint is not null && fingerprint != observed)
                {
                    throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
                }
                fingerprint = observed;
            }
            return fingerprint ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        }
        catch (KeyLoadException error) { LastNativeError = error.Code; throw; }
    }

    private async Task<string> ReadAsync(int index, PhysicalOwnerProbeCallV1 call, CancellationToken token)
    {
        var voter = call.Destination.Owner.VoterIds[index];
        var address = PhysicalOwnerProbeNativeIdentity.Resolve(node, voter, membershipOptions);
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, call.CallerVoter, call.CallerSiloAddress, membershipOptions, token);
        var body = PhysicalOwnerProbeWire.Encode(call);
        using var sourceMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PeerSecret);
        var request = new PartitionMovementGrainServiceRequest(PartitionMovementGrainServiceChannel.PhysicalOwnerProbe,
            true, body, sourceMac.Sign(body, reply: false), call.CallerVoter, call.CallerSiloAddress,
            voter, address.ToParsableString(), Guid.NewGuid(), call.ExpiresAt, string.Empty);
        using var outer = CreateOuterMac();
        request = request with
        {
            SenderSignature = outer.SignNativeRequest(request)
        };
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, voter, request.DestinationSiloAddress, membershipOptions, token);
        var received = await node.BorrowPhysicalOwnerProbeTransport().ProbeOwnerAsync(address, request, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (received is null || received.Nonce != request.Nonce
            || received.Payload.Length > PhysicalOwnerProbeProtocol.MaximumBodyBytes
            || received.SenderSignature is not { Length: PhysicalOwnerProbeProtocol.SignatureCharacters })
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        var reply = received with
        {
            Payload = received.Payload.ToArray()
        };
        if (!outer.VerifyNativeReply(reply))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, call.CallerVoter, call.CallerSiloAddress, membershipOptions, token);
        PhysicalOwnerProbeNativeIdentity.RequireCurrent(node, voter, request.DestinationSiloAddress, membershipOptions, token);
        if (call.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        }
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || !reply.Payload.IsEmpty || reply.Signature is not { Length: EmptyErrorSignatureLength }
                || reply.SafeDetail != PartitionMovementProtocol.Unavailable)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
            }
            throw Errors.Fail(error, PhysicalOwnerProbeProtocol.Unavailable);
        }
        using var targetMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
        if (reply.Payload.IsEmpty || reply.SafeDetail is not null || reply.Signature is null
            || !targetMac.Verify(reply.Payload.Span, reply.Signature, reply: true))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        var original = NativeSerialization.Deserialize<PhysicalOwnerProbeReplyV1>(reply.Payload.Span);
        Verify(index, call, original, token);
        if (original.EndpointDiscovery.SiloAddress != request.DestinationSiloAddress)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
        return original.MembershipFingerprint;
    }

    private PartitionMovementMac CreateOuterMac()
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration);
        var secret = PartitionMovementNativeDiscoveryIdentity.Secret(options, partition.Configuration, control);
        var key = Convert.FromBase64String(secret);
        try
        {
            return new(key, partition.Database.Limits.MaxBatchBytes);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private void Verify(int index, PhysicalOwnerProbeCallV1 call, PhysicalOwnerProbeReplyV1 reply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call.ExpiresAt <= clock.GetUtcNow() || reply.Version != PhysicalOwnerProbeProtocol.Version
            || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce || reply.LocalApplied < EmptyApplied
            || !PhysicalOwnerEntryValidation.Valid(reply.Destination)
            || !PhysicalOwnerEntryValidation.Same(reply.Destination, call.Destination)
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != call.Destination.Owner.VoterIds[index]
            || discovery.ClusterId != options.ClusterId || discovery.Incarnation != call.Destination.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membershipOptions)
            || reply.MembershipFingerprint is not { Length: PhysicalOwnerProbeProtocol.SignatureCharacters }
            || reply.MembershipFingerprint.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref disposed, DisposedResource);
}
