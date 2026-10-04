using KeyLoad.Replication;

namespace KeyLoad.Orleans;

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

/// <summary>Signed replica result, including authenticated failures and the originating request nonce.</summary>
/// <param name="Version">Native envelope protocol version.</param>
/// <param name="Incarnation">Shared replica authority incarnation.</param>
/// <param name="Sender">Configured replying voter.</param>
/// <param name="Recipient">Configured originating voter.</param>
/// <param name="Method">Replica operation being answered.</param>
/// <param name="RuntimeAddress">Exact replying silo generation.</param>
/// <param name="Timestamp">Signed UTC Unix timestamp in milliseconds.</param>
/// <param name="RequestNonce">Nonce of the exact originating request.</param>
/// <param name="Payload">Exact result JSON bytes, or empty on a typed failure.</param>
/// <param name="Error">Safe failure category, or null for success.</param>
/// <param name="SafeDetail">Bounded safe failure detail, or null for success.</param>
/// <param name="Signature">HMAC binding the reply and originating request identity.</param>
[GenerateSerializer, Alias(ReplicaTransportProtocol.ReplyAlias)]
public sealed record ReplicaPeerReply(
    [property: Id(0)] int Version,
    [property: Id(1)] Guid Incarnation,
    [property: Id(2)] string Sender,
    [property: Id(3)] string Recipient,
    [property: Id(4)] ReplicaRpc Method,
    [property: Id(5)] string RuntimeAddress,
    [property: Id(6)] long Timestamp,
    [property: Id(7)] Guid RequestNonce,
    [property: Id(8)] ReadOnlyMemory<byte> Payload,
    [property: Id(9)] ErrorCode? Error,
    [property: Id(10)] string? SafeDetail,
    [property: Id(11)] ReadOnlyMemory<byte> Signature);
