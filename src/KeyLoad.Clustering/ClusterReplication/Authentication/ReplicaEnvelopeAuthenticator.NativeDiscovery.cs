namespace KeyLoad.Clustering;

public sealed partial class ReplicaEnvelopeAuthenticator
{
    internal ReplicaNativeDiscoveryRequest CreateNativeDiscoveryRequest(string voter, string runtimeAddress)
        => nativeDiscovery.CreateNativeDiscoveryRequest(voter, runtimeAddress);

    internal void VerifyNativeDiscoveryRequest(ReplicaNativeDiscoveryRequest request)
        => nativeDiscovery.VerifyNativeDiscoveryRequest(request);

    internal ReplicaNativeDiscoveryReply CreateNativeDiscoveryReply(ReplicaNativeDiscoveryRequest request,
        ReadOnlyMemory<byte> payload, ErrorCode? error = null)
        => nativeDiscovery.CreateNativeDiscoveryReply(request, payload, error);

    internal ReplicaNativeDiscoveryReply VerifyNativeDiscoveryReply(ReplicaNativeDiscoveryRequest request,
        ReplicaNativeDiscoveryReply reply)
        => nativeDiscovery.VerifyNativeDiscoveryReply(request, reply);
}
