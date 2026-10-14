using System.Security.Cryptography;
using KeyLoad.Replication;
namespace KeyLoad.Clustering;

internal sealed class ReplicaNativeDiscoveryAuthentication
{
    private const int EmptyDiscoverySignatureLength = 0;
    private readonly ReplicaEnvelopeAuthenticator owner;
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly ReplicaEnvelopeFreshness freshness;
    private readonly ReplicaMessageMac mac;
    private readonly HashSet<string> voters;

    internal ReplicaNativeDiscoveryAuthentication(ReplicaEnvelopeAuthenticator owner,
        Microsoft.Extensions.Options.IOptions<ReplicaConfiguration> configurationOptions,
        ReplicaSiloDiscoveryState local, ReplicaEnvelopeFreshness freshness,
        ReplicaMessageMac mac, HashSet<string> voters)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(configurationOptions);
        ArgumentNullException.ThrowIfNull(configurationOptions.Value);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(freshness);
        ArgumentNullException.ThrowIfNull(mac);
        ArgumentNullException.ThrowIfNull(voters);
        this.owner = owner;
        configuration = configurationOptions.Value;
        this.local = local;
        this.freshness = freshness;
        this.mac = mac;
        this.voters = voters;
    }

    internal ReplicaNativeDiscoveryRequest CreateNativeDiscoveryRequest(string voter, string runtimeAddress)
    {
        mac.RequireOpen();
        if (!voters.Contains(voter) || !NativeDiscoveryAddress(runtimeAddress))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
        var expires = DateTimeOffset.FromUnixTimeMilliseconds(freshness.Now()).Add(configuration.RpcTimeout);
        var request = new ReplicaNativeDiscoveryRequest(ReplicaNativeDiscoveryProtocol.Version, configuration.LocalId,
            local.RuntimeAddress, voter, runtimeAddress, Guid.NewGuid(), expires, string.Empty);
        return request with
        {
            Signature = owner.SignDiscovery(NativeDiscoveryFrame(request with
            {
                Signature = string.Empty
            },
            ReplicaNativeDiscoveryProtocol.RequestPurpose), request.Nonce)
        };
    }

    internal void VerifyNativeDiscoveryRequest(ReplicaNativeDiscoveryRequest request)
    {
        mac.RequireOpen();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(freshness.Now());
        if (request is null || request.Version != ReplicaNativeDiscoveryProtocol.Version || request.Nonce == Guid.Empty
            || !NativeDiscoveryIdentity(request.CallerVoter) || !voters.Contains(request.CallerVoter)
            || request.DestinationVoter != configuration.LocalId || !NativeDiscoveryIdentity(request.DestinationVoter)
            || !ReplicaEnvelopeValidation.Address(request.CallerSiloAddress)
            || !ReplicaEnvelopeValidation.Address(request.DestinationSiloAddress)
            || request.DestinationSiloAddress != local.RuntimeAddress || request.ExpiresAt.Offset != TimeSpan.Zero
            || request.ExpiresAt <= now || !NativeDiscoverySignature(request.Signature)
            || !NativeDiscoveryIssuance(request.ExpiresAt))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
        }
        owner.VerifyDiscovery(request.CallerVoter, NativeDiscoveryFrame(request with
        {
            Signature = string.Empty
        },
            ReplicaNativeDiscoveryProtocol.RequestPurpose), request.Nonce, request.Signature);
    }

    internal ReplicaNativeDiscoveryReply CreateNativeDiscoveryReply(ReplicaNativeDiscoveryRequest request,
        ReadOnlyMemory<byte> payload, ErrorCode? error = null)
    {
        mac.RequireOpen();
        ArgumentNullException.ThrowIfNull(request);
        if (payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes
            || error is { } code && (!Enum.IsDefined(code) || !payload.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }
        var owned = payload.ToArray();
        var original = error is null ? owner.SignDiscovery(owned, request.Nonce) : string.Empty;
        var reply = new ReplicaNativeDiscoveryReply(owned, original, request.Nonce, error, string.Empty);
        return reply with
        {
            Signature = Convert.ToHexStringLower(mac.Discovery(configuration.Incarnation,
            configuration.LocalId, request.Nonce, NativeDiscoveryFrame(reply, ReplicaNativeDiscoveryProtocol.ReplyPurpose)))
        };
    }

    internal ReplicaNativeDiscoveryReply VerifyNativeDiscoveryReply(ReplicaNativeDiscoveryRequest request,
        ReplicaNativeDiscoveryReply reply)
    {
        mac.RequireOpen();
        ArgumentNullException.ThrowIfNull(request);
        if (reply is null || reply.Nonce != request.Nonce || reply.Payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes
            || !NativeDiscoverySignature(reply.Signature)
            || reply.Error is { } code && (!Enum.IsDefined(code) || !reply.Payload.IsEmpty || reply.DiscoverySignature is not { Length: EmptyDiscoverySignatureLength })
            || reply.Error is null && (!NativeDiscoverySignature(reply.DiscoverySignature) || reply.Payload.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
        }
        var owned = reply with
        {
            Payload = reply.Payload.ToArray()
        };
        owner.VerifyDiscovery(request.DestinationVoter, NativeDiscoveryFrame(owned with
        {
            Signature = string.Empty
        },
            ReplicaNativeDiscoveryProtocol.ReplyPurpose), request.Nonce, owned.Signature);
        if (owned.Error is null)
        {
            owner.VerifyDiscovery(request.DestinationVoter, owned.Payload.Span, request.Nonce, owned.DiscoverySignature);
        }
        return owned;
    }

    private bool NativeDiscoveryIssuance(DateTimeOffset expires)
    {
        try
        {
            return freshness.Includes(expires.Subtract(configuration.RpcTimeout).ToUnixTimeMilliseconds());
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool NativeDiscoveryAddress(string value)
    {
        if (!ReplicaEnvelopeValidation.Address(value))
        {
            return false;
        }
        return SiloAddress.TryParse(value, out var address)
            && address.ToParsableString() == value;
    }

    private static bool NativeDiscoveryIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= ReplicaTransportProtocol.MaximumIdentityCharacters;

    private static bool NativeDiscoverySignature(string? value)
        => value is { Length: ReplicaTransportProtocol.HashHexCharacters } && value.All(Uri.IsHexDigit);

    private static byte[] NativeDiscoveryFrame<T>(T value, string purpose)
        => NativeSerialization.Serialize(new ReplicaNativeDiscoveryMacFrame(purpose,
            SHA256.HashData(NativeSerialization.Serialize(value))));
}
