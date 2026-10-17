using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class EpochReplicaMac
{
    private const int NoError = -1;

    internal static byte[] Request(ReplicaPeerOptions options, ReplicaPeerEnvelope request, string purpose,
        int macVersion = ReplicaTransportProtocol.Version)
        => Message(options, purpose, request.Incarnation, request.Sender, request.Recipient, request.Method,
            request.RuntimeAddress, request.Timestamp, request.Nonce, request.Payload.Span, null, null, macVersion);

    internal static byte[] Reply(ReplicaPeerOptions options, ReplicaPeerReply reply, string purpose,
        int macVersion = ReplicaTransportProtocol.Version)
        => Message(options, purpose, reply.Incarnation, reply.Sender, reply.Recipient, reply.Method,
            reply.RuntimeAddress, reply.Timestamp, reply.RequestNonce, reply.Payload.Span, reply.Error, reply.SafeDetail, macVersion);

    internal static string Discovery(ReplicaPeerOptions options, Guid incarnation, string voter, Guid nonce,
        ReadOnlySpan<byte> payload, string purpose)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(purpose);
        writer.Write(ReplicaTransportProtocol.DiscoveryMacVersion);
        writer.Write(options.ClusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(voter);
        writer.Write(nonce.ToByteArray());
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return Convert.ToHexStringLower(HMACSHA256.HashData(options.Secret.Span,
            buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static byte[] Message(ReplicaPeerOptions options, string purpose, Guid incarnation, string sender,
        string recipient, ReplicaRpc method, string address, long timestamp, Guid nonce, ReadOnlySpan<byte> payload,
        ErrorCode? error, string? detail, int macVersion)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(purpose);
        writer.Write(macVersion);
        writer.Write(options.ClusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(sender);
        writer.Write(recipient);
        writer.Write((int)method);
        writer.Write(address);
        writer.Write(timestamp);
        writer.Write(nonce.ToByteArray());
        writer.Write(error is null ? NoError : (int)error.Value);
        writer.Write(detail ?? string.Empty);
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return HMACSHA256.HashData(options.Secret.Span, buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }
}
