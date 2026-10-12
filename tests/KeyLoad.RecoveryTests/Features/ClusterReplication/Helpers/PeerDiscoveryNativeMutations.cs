using KeyLoad.Orleans;
namespace KeyLoad.RecoveryTests;

internal static class PeerDiscoveryNativeMutations
{
    internal static ReplicaNativeDiscoveryRequest Apply(ReplicaNativeDiscoveryRequest request, PeerDiscoveryNativeMutation mutation)
        => mutation switch
        {
            PeerDiscoveryNativeMutation.Version => request with { Version = request.Version + 1 },
            PeerDiscoveryNativeMutation.Caller => request with { CallerVoter = PeerDiscoveryNativeCluster.ThirdVoter },
            PeerDiscoveryNativeMutation.CallerAddress => request with { CallerSiloAddress = request.DestinationSiloAddress },
            PeerDiscoveryNativeMutation.Destination => request with { DestinationVoter = PeerDiscoveryNativeCluster.ThirdVoter },
            PeerDiscoveryNativeMutation.DestinationAddress => request with { DestinationSiloAddress = request.CallerSiloAddress },
            PeerDiscoveryNativeMutation.Nonce => request with { Nonce = Guid.Empty },
            PeerDiscoveryNativeMutation.Expiry => request with { ExpiresAt = DateTimeOffset.MinValue },
            PeerDiscoveryNativeMutation.Signature => request with { Signature = new string('0', request.Signature.Length) },
            PeerDiscoveryNativeMutation.MissingCaller => request with { CallerVoter = string.Empty },
            PeerDiscoveryNativeMutation.MissingCallerAddress => request with { CallerSiloAddress = string.Empty },
            PeerDiscoveryNativeMutation.MissingDestination => request with { DestinationVoter = string.Empty },
            PeerDiscoveryNativeMutation.MissingDestinationAddress => request with { DestinationSiloAddress = string.Empty },
            PeerDiscoveryNativeMutation.MissingSignature => request with { Signature = string.Empty },
            PeerDiscoveryNativeMutation.DuplicateSignature => request with { Signature = request.Signature + request.Signature },
            PeerDiscoveryNativeMutation.OversizedCaller => request with { CallerVoter = new string('v', ReplicaTransportProtocol.MaximumIdentityCharacters + 1) },
            PeerDiscoveryNativeMutation.MalformedSignature => request with { Signature = new string('z', request.Signature.Length) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
}
