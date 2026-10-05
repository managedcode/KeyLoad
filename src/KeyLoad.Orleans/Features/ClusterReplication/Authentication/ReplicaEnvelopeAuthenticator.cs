using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Authenticates exact-byte RPCs and replies within the configured fixed-voter scope.</summary>
public sealed class ReplicaEnvelopeAuthenticator : IDisposable
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly TimeProvider clock;
    private readonly ReplicaMessageMac mac;
    private readonly ReplicaReplayWindow replay;
    private readonly HashSet<string> voters;
    private readonly int maximumControlPayloadBytes;
    private readonly ReplicaReplayAdmissionDiagnostics? diagnostics;
    private readonly DatabaseEngine? canonicalDatabase;
    private readonly ReplicaTransportOptions settings;

    /// <summary>Creates authentication for one node, fixed voter scope and system clock.</summary>
    /// <param name="configurationOptions">The node identity, voter set, incarnation and payload limits.</param>
    /// <param name="peerOptions">The fixed-cluster signing secret, control budget and replay capacities.</param>
    /// <param name="local">The actual local Orleans runtime generation.</param>
    /// <param name="clock">The system clock used for envelope freshness.</param>
    /// <param name="transportOptions">Centrally validated envelope freshness and retry settings.</param>
    /// <param name="logger">Optional closed numeric replay-admission diagnostics.</param>
    /// <param name="canonicalDatabase">Optional borrowed canonical authority; required for non-null native operations.</param>
    public ReplicaEnvelopeAuthenticator(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> peerOptions,
        ReplicaSiloDiscoveryState local, TimeProvider clock, IOptions<ReplicaTransportOptions> transportOptions,
        ILogger<ReplicaEnvelopeAuthenticator>? logger = null,
        DatabaseEngine? canonicalDatabase = null)
    {
        var configuration = configurationOptions.Value;
        var options = peerOptions.Value;
        settings = transportOptions.Value;
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(configuration);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(clock);
        if (canonicalDatabase is not null && canonicalDatabase.Store.Identity.Incarnation != configuration.Incarnation)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.InvalidPeer);
        }
        this.configuration = configurationOptions.Value;
        this.local = local;
        this.clock = clock;
        this.canonicalDatabase = canonicalDatabase;
        voters = new(configuration.VoterIds, StringComparer.Ordinal);
        mac = new(options.Secret, options.ClusterId);
        replay = new(configuration.VoterIds, peerOptions, transportOptions);
        maximumControlPayloadBytes = options.MaxControlPayloadBytes;
        if (logger is not null)
        {
            diagnostics = new(logger, configuration.VoterIds.Length, transportOptions);
            diagnostics.Configured(options.ReplayLimits);
        }
    }

    /// <summary>Maximum exact-byte protocol payload including bounded metadata.</summary>
    /// <value>The configured append bound plus the fixed metadata allowance.</value>
    public int MaximumPayloadBytes => checked(configuration.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes);

    /// <summary>Required native Orleans body bound, including envelope fields.</summary>
    /// <value>The payload bound plus the encoded envelope overhead.</value>
    public int MaximumMessageBodyBytes => checked(MaximumPayloadBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes);

    /// <summary>Signs an RPC for one configured voter and its discovered runtime generation.</summary>
    /// <param name="recipient">A voter in the fixed configured voter set.</param>
    /// <param name="method">The replica RPC method to authenticate.</param>
    /// <param name="runtimeAddress">The recipient's current discovered Orleans generation.</param>
    /// <param name="payload">The exact bounded payload bytes to sign.</param>
    /// <returns>A signed request with a fresh nonce and timestamp.</returns>
    public ReplicaPeerEnvelope SignRequest(string recipient, ReplicaRpc method, string runtimeAddress, ReadOnlyMemory<byte> payload)
    {
        ValidatePayload(payload);
        if (!voters.Contains(recipient) || !Enum.IsDefined(method) || !ValidAddress(runtimeAddress))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }

        var request = new ReplicaPeerEnvelope(ReplicaTransportProtocol.Version, configuration.Incarnation,
            configuration.LocalId, recipient, method, runtimeAddress, Now(), Guid.NewGuid(), payload, ReadOnlyMemory<byte>.Empty);
        return request with { Signature = mac.Request(request) };
    }

    /// <summary>Checks borrowed bytes, then owns and rechecks the exact admitted request before payload dispatch.</summary>
    /// <param name="request">The candidate request whose original buffers must not flow across an await.</param>
    /// <returns>The fully verified owned payload and signature snapshot; retain this result for dispatch.</returns>
    public ReplicaPeerEnvelope VerifyRequest(ReplicaPeerEnvelope request)
    {
        _ = ClassifyBorrowedRequest(request);
        var owned = request with { Payload = request.Payload.ToArray(), Signature = request.Signature.ToArray() };
        ValidateRequestScope(owned);
        var pool = ValidateRequestPayload(owned);
        if (!replay.TryAdmit(owned.Sender, owned.Nonce, owned.Timestamp, Now(), pool, owned.Method, out var failure))
        {
            diagnostics?.Report(failure);
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.ReplayCapacityExceeded);
        }
        return owned;
    }

    // The same production pre-copy stage is independently measurable; no nonce is retained here.
    internal ReplicaReplayPool ClassifyBorrowedRequest(ReplicaPeerEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestScope(request);
        replay.RejectReplay(request.Sender, request.Nonce, Now());
        return ValidateRequestPayload(request);
    }

    private void ValidateRequestScope(ReplicaPeerEnvelope request)
    {
        ValidatePayload(request.Payload);
        if (request.Version != ReplicaTransportProtocol.Version || request.Incarnation != configuration.Incarnation
            || request.Recipient != configuration.LocalId || !voters.Contains(request.Sender)
            || request.RuntimeAddress != local.RuntimeAddress || !Enum.IsDefined(request.Method)
            || request.Nonce == Guid.Empty || !Fresh(request.Timestamp) || !ValidSignature(request.Signature)
            || !CryptographicOperations.FixedTimeEquals(request.Signature.Span, mac.Request(request)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaProtocol.InvalidPeer);
        }
    }

    private ReplicaReplayPool ValidateRequestPayload(ReplicaPeerEnvelope request)
    {
        ReplicaSenderValidator.Validate(request.Method, request.Payload, request.Sender, configuration);
        var pool = ReplicaReplayClassifier.Classify(request.Method, request.Payload, configuration,
            maximumControlPayloadBytes, canonicalDatabase);
        if (!Fresh(request.Timestamp))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaProtocol.InvalidPeer);
        }
        return pool;
    }

    /// <summary>Signs successful bytes or a bounded error without serializing exception objects.</summary>
    /// <param name="request">The originating request, or null when no valid request exists.</param>
    /// <param name="payload">The bounded exact response bytes.</param>
    /// <param name="error">An optional safe protocol error code.</param>
    /// <param name="detail">An optional bounded diagnostic detail.</param>
    /// <returns>A reply signed for the selected recipient and originating request.</returns>
    public ReplicaPeerReply CreateReply(ReplicaPeerEnvelope? request, ReadOnlyMemory<byte> payload, ErrorCode? error = null, string? detail = null)
    {
        ValidatePayload(payload);
        var recipient = request is not null && voters.Contains(request.Sender) ? request.Sender : configuration.LocalId;
        var method = request is not null && Enum.IsDefined(request.Method) ? request.Method : ReplicaRpc.ReadBarrier;
        var safeDetail = detail is { Length: > ReplicaTransportProtocol.MaximumDetailCharacters }
            ? ReplicaTransportProtocol.EndpointFailure : detail;
        var reply = new ReplicaPeerReply(ReplicaTransportProtocol.Version, configuration.Incarnation,
            configuration.LocalId, recipient, method, local.RuntimeAddress, Now(), request?.Nonce ?? Guid.Empty,
            payload, error, safeDetail, ReadOnlyMemory<byte>.Empty);
        return reply with { Signature = mac.Reply(reply) };
    }

    /// <summary>Verifies that the signed result belongs to this exact request and destination generation.</summary>
    /// <param name="request">The request that initiated the call.</param>
    /// <param name="reply">The candidate response envelope.</param>
    public void VerifyReply(ReplicaPeerEnvelope request, ReplicaPeerReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(request);
        ValidatePayload(reply.Payload);
        if (reply.Version != ReplicaTransportProtocol.Version || reply.Incarnation != request.Incarnation
            || reply.Sender != request.Recipient || reply.Recipient != request.Sender
            || reply.Method != request.Method || reply.RuntimeAddress != request.RuntimeAddress
            || reply.RequestNonce != request.Nonce || !Fresh(reply.Timestamp) || !ValidSignature(reply.Signature)
            || reply.SafeDetail is { Length: > ReplicaTransportProtocol.MaximumDetailCharacters }
            || (reply.Error is { } error && (!Enum.IsDefined(error) || reply.Payload.Length != 0))
            || (reply.Error is null && reply.SafeDetail is not null)
            || !CryptographicOperations.FixedTimeEquals(reply.Signature.Span, mac.Reply(reply)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaProtocol.InvalidPeer);
        }
    }

    /// <summary>Signs exact discovery response bytes and the authenticated GET nonce.</summary>
    /// <param name="payload">The exact bounded discovery document bytes.</param>
    /// <param name="requestNonce">The nonempty nonce from the authenticated discovery request.</param>
    /// <returns>A hexadecimal HMAC signature for the discovery response.</returns>
    public string SignDiscovery(ReadOnlySpan<byte> payload, Guid requestNonce)
    {
        ValidateDiscovery(payload, requestNonce);
        return Convert.ToHexStringLower(mac.Discovery(configuration.Incarnation, configuration.LocalId, requestNonce, payload));
    }

    /// <summary>Rejects substituted discovery responses before accepting their runtime address.</summary>
    /// <param name="voterId">The fixed voter whose endpoint is being discovered.</param>
    /// <param name="payload">The exact response bytes received from that voter.</param>
    /// <param name="requestNonce">The nonce sent with the matching discovery request.</param>
    /// <param name="signature">The response signature supplied by the endpoint.</param>
    public void VerifyDiscovery(string voterId, ReadOnlySpan<byte> payload, Guid requestNonce, string signature)
    {
        ValidateDiscovery(payload, requestNonce);
        if (!voters.Contains(voterId) || signature is null
            || signature.Length != ReplicaTransportProtocol.HashHexCharacters || !signature.All(Uri.IsHexDigit)
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
                mac.Discovery(configuration.Incarnation, voterId, requestNonce, payload)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
        }
    }

    private void ValidatePayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaximumPayloadBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
        }
    }

    private static void ValidateDiscovery(ReadOnlySpan<byte> payload, Guid nonce)
    {
        if (payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes || nonce == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeMilliseconds();

    private bool Fresh(long timestamp)
    {
        var lifetime = checked((long)settings.EnvelopeLifetime.TotalMilliseconds);
        var now = Now();
        return timestamp >= now - lifetime && timestamp <= now + lifetime;
    }

    private static bool ValidAddress(string address) => !string.IsNullOrWhiteSpace(address)
        && address.Length <= ReplicaTransportProtocol.MaximumAddressCharacters;

    private static bool ValidSignature(ReadOnlyMemory<byte> signature) => signature.Length == ReplicaTransportProtocol.HashBytes;

    /// <inheritdoc />
    public void Dispose() => mac.Dispose();
}
