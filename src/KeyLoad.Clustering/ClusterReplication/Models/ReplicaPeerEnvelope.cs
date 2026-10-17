using KeyLoad.Replication;

namespace KeyLoad.Clustering;

/// <summary>Authenticated exact-byte replica request, addressed to one runtime generation.</summary>
/// <param name="Version">Native envelope protocol version.</param>
/// <param name="Incarnation">Shared replica authority incarnation.</param>
/// <param name="Sender">Configured originating voter.</param>
/// <param name="Recipient">Configured destination voter.</param>
/// <param name="Method">Authenticated replica operation.</param>
/// <param name="RuntimeAddress">Exact destination silo generation.</param>
/// <param name="Timestamp">Signed UTC Unix timestamp in milliseconds.</param>
/// <param name="Nonce">Unique bounded-lifetime replay identity.</param>
/// <param name="Payload">Exact read-only UTF-8 replica JSON bytes.</param>
/// <param name="Signature">HMAC binding the envelope scope and payload hash.</param>
[GenerateSerializer, Alias(ReplicaTransportProtocol.RequestAlias)]
public sealed record ReplicaPeerEnvelope(
    [property: Id(0)] int Version,
    [property: Id(1)] Guid Incarnation,
    [property: Id(2)] string Sender,
    [property: Id(3)] string Recipient,
    [property: Id(4)] ReplicaRpc Method,
    [property: Id(5)] string RuntimeAddress,
    [property: Id(6)] long Timestamp,
    [property: Id(7)] Guid Nonce,
    [property: Id(8)] ReadOnlyMemory<byte> Payload,
    [property: Id(9)] ReadOnlyMemory<byte> Signature);
