using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorOutgoingCall : IDisposable
{
    private const char Base64Padding = '=';
    private const char Base64Plus = '+';
    private const char Base64Slash = '/';
    private const char UrlMinus = '-';
    private const char UrlUnderscore = '_';

    private EventVectorRemoteCall? call;

    private EventVectorOutgoingCall(EventVectorRemoteCall call) { this.call = call; }

    internal EventVectorRemoteCall OriginalCall => call
        ?? throw new ObjectDisposedException(nameof(EventVectorOutgoingCall));

    internal static EventVectorOutgoingCall Create<T>(OrleansNode node, PartitionHost partition,
        NodeOptions options, EventVectorRemoteAction action, T originalPayload,
        DateTimeOffset originalExpiresAt, ReadExecutionBudget work, EventVectorRemoteEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(originalPayload);
        work.Check();
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);
        if (!discovery.TransportReady || discovery.Incarnation != control.Owner.Incarnation
            || !control.Owner.VoterIds.Contains(discovery.VoterId, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        var measured = NativeSerialization.Measure(originalPayload);
        work.ChargeBytes(measured);
        var payload = encoding.Encode(originalPayload);
        if (payload.Length != measured)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
        var transferred = false;
        try
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(
                ReplicaMembershipAuthorityProtocol.NonceBytes)).TrimEnd(Base64Padding)
                .Replace(Base64Plus, UrlMinus).Replace(Base64Slash, UrlUnderscore);
            var actualCall = new EventVectorRemoteCall(EventVectorRemoteProtocol.Version, Guid.NewGuid(), nonce, originalExpiresAt,
                discovery.VoterId, discovery.SiloAddress, control.Owner,
                EventVectorRemoteProtocol.Purpose, action, payload);
            var owner = new EventVectorOutgoingCall(actualCall);
            transferred = true;
            return owner;
        }
        finally
        {
            if (!transferred)
            { CryptographicOperations.ZeroMemory(payload); }
        }
    }

    public void Dispose()
    {
        if (call is not { } owned)
        { return; }
        call = null;
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(owned.OriginalNativeRequest).Span);
    }
}
