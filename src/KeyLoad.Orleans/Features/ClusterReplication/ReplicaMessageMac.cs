using System.Security.Cryptography;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMessageMac(ReadOnlyMemory<byte> credential, string clusterId) : IDisposable
{
    private readonly byte[] secret = credential.ToArray();

    public byte[] Request(ReplicaPeerEnvelope request) => Message(ReplicaTransportProtocol.RequestPurpose,
        request.Incarnation, request.Sender, request.Recipient, request.Method, request.RuntimeAddress,
        request.Timestamp, request.Nonce, request.Payload.Span, null, null);

    public byte[] Reply(ReplicaPeerReply reply) => Message(ReplicaTransportProtocol.ReplyPurpose,
        reply.Incarnation, reply.Sender, reply.Recipient, reply.Method, reply.RuntimeAddress,
        reply.Timestamp, reply.RequestNonce, reply.Payload.Span, reply.Error, reply.SafeDetail);

    public byte[] Discovery(Guid incarnation, string voterId, Guid requestNonce, ReadOnlySpan<byte> payload)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(ReplicaTransportProtocol.DiscoveryPurpose);
        writer.Write(ReplicaTransportProtocol.Version);
        writer.Write(clusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(voterId);
        writer.Write(requestNonce.ToByteArray());
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return Sign(buffer);
    }

    private byte[] Message(string purpose, Guid incarnation, string sender, string recipient, ReplicaRpc method,
        string address, long timestamp, Guid nonce, ReadOnlySpan<byte> payload, ErrorCode? error, string? detail)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(purpose);
        writer.Write(ReplicaTransportProtocol.Version);
        writer.Write(clusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(sender);
        writer.Write(recipient);
        writer.Write((int)method);
        writer.Write(address);
        writer.Write(timestamp);
        writer.Write(nonce.ToByteArray());
        writer.Write(error is null ? ReplicaTransportProtocol.NoError : (int)error.Value);
        writer.Write(detail ?? string.Empty);
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return Sign(buffer);
    }

    private byte[] Sign(MemoryStream buffer)
    {
        if (buffer.Length > ReplicaTransportProtocol.MaximumMetadataBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidPeer);
        }

        return HMACSHA256.HashData(secret, buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(secret);
}
