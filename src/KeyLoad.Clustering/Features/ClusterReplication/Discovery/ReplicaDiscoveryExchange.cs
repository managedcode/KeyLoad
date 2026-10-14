using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Authenticates exact metadata through the unique current native silo generation.</summary>
internal sealed class ReplicaDiscoveryExchange(IOptions<ReplicaConfiguration> configurationOptions,
    IOptions<ReplicaPeerOptions> peerSettings, ReplicaEnvelopeAuthenticator authentication, TimeProvider clock,
    ReplicaNativeDiscoveryClient native, ReplicaNativeDiscoveryMembership membership, ReplicaDiscoveryProbe? probe = null)
    : IDisposable
{
    private const int Open = 0;
    private const int Closed = 1;
    private int disposed;

    internal async Task<ReplicaDiscoveryObservation?> DiscoverAsync(string voterId, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != Open, this);
        token.ThrowIfCancellationRequested();
        var address = membership.Resolve(voterId);
        if (address is null)
        {
            return null;
        }
        var request = authentication.CreateNativeDiscoveryRequest(voterId, address.ToParsableString());
        probe?.BeforeSend(request, token);
        if (probe?.BeforeSendAsync is { } beforeSend)
        {
            await beforeSend(request, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (!membership.IsCurrent(voterId, address))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        ReplicaNativeDiscoveryReply? received;
        try
        {
            received = await native.DiscoverAsync(address, request, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OrleansException or TimeoutException or ObjectDisposedException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
        token.ThrowIfCancellationRequested();
        if (received is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
        }
        if (!membership.IsCurrent(voterId, address))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        if (probe?.Received is { } transform)
        {
            received = transform(request, received, token)
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
        }
        token.ThrowIfCancellationRequested();
        var reply = authentication.VerifyNativeDiscoveryReply(request, received);
        if (!membership.IsCurrent(voterId, address))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, ReplicaTransportProtocol.InvalidDiscovery);
        }
        var discovered = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(reply.Payload.Span);
        var observation = ReplicaDiscoveryIdentity.CreateObservation(voterId, discovered,
            configurationOptions.Value, peerSettings.Value, clock);
        if (!observation.Address.Equals(address))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        probe?.Verified(voterId, request.Nonce, discovered, token);
        token.ThrowIfCancellationRequested();
        if (!membership.IsCurrent(voterId, address))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        return observation;
    }

    public void Dispose() => Volatile.Write(ref disposed, Closed);
}
