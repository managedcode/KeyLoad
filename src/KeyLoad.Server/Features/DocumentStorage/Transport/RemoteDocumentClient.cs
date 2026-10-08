using KeyLoad.Orleans;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentClient : IDisposable
{
    private const int NoFailures = 0;
    private const int SelectedVoter = 0;
    private const int SignatureCount = 1;
    private const long EmptyApplied = 0;
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly TimeProvider clock;
    private readonly HttpClient http;
    private readonly SocketsHttpHandler handler;
    private readonly ReplicaMembershipAuthorityAddressPins pins;

    internal RemoteDocumentClient(IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        this.options = options.Value;
        this.membership = membership;
        this.clock = clock;
        pins = new(this.options.MembershipAuthority.TrustedGroupSiloEndpoints, membership);
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
        http = created ?? throw new InvalidOperationException(RemoteDocumentProtocol.Unavailable);
    }

    internal async Task<OwnedDocumentReadResultV1> ReadAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(call, cancellationToken).ConfigureAwait(false);
        return reply.Result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    internal async Task<PartitionQueryLeafResultV1> ReadLeafAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(call, cancellationToken).ConfigureAwait(false);
        return reply.QueryLeaf ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private async Task<RemoteDocumentReplyV1> ReadReplyAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = call.Fence.Destination;
        var body = RemoteDocumentWire.Encode(call);
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(destination.Endpoints[SelectedVoter]), RemoteDocumentProtocol.Path));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
        request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, sourceMac.Sign(body, reply: false));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        var encoded = await RemoteDocumentWire.ReadReplyAsync(response.Content, cancellationToken, RemoteDocumentWire.ReplyMaximum(call)).ConfigureAwait(false);
        var signatures = response.Headers.TryGetValues(RemoteDocumentProtocol.SignatureHeader, out var values)
            ? values.Take(SignatureCount + SignatureCount).ToArray() : [];
        using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.MembershipAuthority.TrustedGroupPeerSecret);
        if (signatures.Length != SignatureCount || !targetMac.Verify(encoded, signatures[SelectedVoter], reply: true))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(encoded);
        Verify(call, reply, cancellationToken);
        var address = SiloAddress.FromParsableString(reply.EndpointDiscovery.SiloAddress);
        await pins.PinCallerAsync(SelectedVoter, address.Endpoint.Address, cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable); }
        return reply;
    }

    private void Verify(RemoteDocumentCallV1 call, RemoteDocumentReplyV1 reply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = call.Fence.Destination.Owner;
        if (call.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != owner.VoterIds[SelectedVoter] || discovery.Incarnation != owner.Incarnation
            || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membership)
            || SiloAddress.FromParsableString(discovery.SiloAddress).Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort
            || (reply.Error is null) != (reply.Result is not null || reply.QueryLeaf is not null)
            || reply.Result is not null && (call.Request is null || reply.QueryLeaf is not null)
            || reply.QueryLeaf is not null && call.QueryLeaf is null
            || reply.Error is null && reply.SafeDetail is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        if (reply.Result is { } result && (result.NodeId == Guid.Empty || result.Incarnation != owner.Incarnation
            || result.AppliedPosition < EmptyApplied || result.StorePosition < EmptyApplied
            || result.Placement.PhysicalShardId != owner.PhysicalShardId
            || result.Placement.Incarnation != owner.Incarnation || result.Placement.Partition != RemotePartitionQueryScope.Partition(call)
            || result.Placement.PlacementEpoch != owner.PlacementEpoch
            || !result.Placement.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        if (reply.QueryLeaf is { } leaf && (leaf.NodeId == Guid.Empty || leaf.Incarnation != owner.Incarnation
            || leaf.ReadGeneration < EmptyApplied || leaf.Partition != RemotePartitionQueryScope.Partition(call)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        if (reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
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
