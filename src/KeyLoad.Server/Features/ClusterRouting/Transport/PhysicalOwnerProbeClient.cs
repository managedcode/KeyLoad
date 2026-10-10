using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeClient : IDisposable
{
    private const int FirstVoter = 0;
    private const int UnobservedHttpStatus = 0;
    private int lastHttpStatus;

    internal int LastHttpStatus => Volatile.Read(ref lastHttpStatus);
    private const int NoFailures = 0;
    private const long EmptyApplied = 0;
    private const int SingleSignature = 1;
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly TimeProvider clock;
    private readonly HttpClient http;
    private readonly SocketsHttpHandler handler;
    private readonly ReplicaMembershipAuthorityAddressPins pins;

    internal PhysicalOwnerProbeClient(IOptions<NodeOptions> nodeOptions, OrleansNode node,
        PartitionHost partition, IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock)
    {
        options = nodeOptions.Value;
        this.membershipOptions = membershipOptions;
        this.node = node;
        this.partition = partition;
        this.clock = clock;
        pins = new(options.MembershipAuthority.TrustedGroupSiloEndpoints, membershipOptions);
        var failures = new List<Exception>();
        HttpClient? created = null;
        handler = new()
        { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.None };
        ServerFailureObserver.Observe(() => created = new HttpClient(handler, disposeHandler: false)
        { Timeout = Timeout.InfiniteTimeSpan }, failures);
        if (failures.Count != NoFailures)
        {
            try
            { handler.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            try
            { pins.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        http = created ?? throw new InvalidOperationException(PhysicalOwnerProbeProtocol.Unavailable);
    }

    internal async Task<string> VerifyAllAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (!options.MembershipAuthority.RegisterPhysicalOwners
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable); }
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        string? fingerprint = null;
        for (var index = FirstVoter; index < PhysicalOwnerDirectoryProtocol.VoterCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = new PhysicalOwnerProbeCallV1(PhysicalOwnerProbeProtocol.Version, Guid.NewGuid(),
                control, destination, partition.Configuration.LocalId, discovery.SiloAddress,
                Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(ReplicaMembershipAuthorityProtocol.NonceBytes)), clock.GetUtcNow(), expiresAt);
            var observed = await ReadAsync(index, call, cancellationToken).ConfigureAwait(false);
            if (fingerprint is not null && fingerprint != observed)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
            fingerprint = observed;
        }
        return fingerprint ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
    }

    private async Task<string> ReadAsync(int index, PhysicalOwnerProbeCallV1 call, CancellationToken cancellationToken)
    {
        var body = PhysicalOwnerProbeWire.Encode(call);
        using var sourceMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.PeerSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(call.Destination.Endpoints[index]), PhysicalOwnerProbeProtocol.Path));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new(PhysicalOwnerProbeProtocol.ContentType);
        request.Headers.Add(PhysicalOwnerProbeProtocol.SignatureHeader, sourceMac.Sign(body, reply: false));
        Volatile.Write(ref lastHttpStatus, UnobservedHttpStatus);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref lastHttpStatus, (int)response.StatusCode);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        var encoded = await PhysicalOwnerProbeWire.ReadReplyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var signatures = response.Headers.TryGetValues(PhysicalOwnerProbeProtocol.SignatureHeader, out var values)
            ? values.Take(SingleSignature + SingleSignature).ToArray() : [];
        using var targetMac = PhysicalOwnerProbeMac.FromConfiguredSecret(options.MembershipAuthority.TrustedGroupPeerSecret);
        if (signatures.Length != SingleSignature || !targetMac.Verify(encoded, signatures[FirstVoter], reply: true))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
        var reply = NativeSerialization.Deserialize<PhysicalOwnerProbeReplyV1>(encoded);
        Verify(index, call, reply, cancellationToken);
        var address = SiloAddress.FromParsableString(reply.EndpointDiscovery.SiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
        await pins.PinCallerAsync(index, address.Endpoint.Address, cancellationToken).ConfigureAwait(false);
        return reply.MembershipFingerprint;
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
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membershipOptions)
            || reply.MembershipFingerprint is not { Length: PhysicalOwnerProbeProtocol.SignatureCharacters }
            || reply.MembershipFingerprint.Any(character => !char.IsAsciiHexDigitLower(character)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        { http.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { handler.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { pins.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
