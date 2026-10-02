using System.Text;

namespace KeyLoad.Orleans;

/// <summary>Stable wire identities and bounded transport metadata.</summary>
public static class ReplicaTransportProtocol
{
    /// <summary>Authenticated envelope format version.</summary>
    public const int Version = 1;
    /// <summary>Orleans replica service interface version.</summary>
    public const ushort InterfaceVersion = 1;
    /// <summary>Required cluster peer credential length.</summary>
    public const int SecretBytes = 32;
    /// <summary>SHA256 and HMAC-SHA256 output length.</summary>
    public const int HashBytes = 32;
    /// <summary>Discovery signature hexadecimal length.</summary>
    public const int HashHexCharacters = HashBytes * 2;
    /// <summary>Maximum configured voter identity length in UTF16 code units.</summary>
    public const int MaximumIdentityCharacters = 2_048;
    /// <summary>Maximum cluster identity length in UTF16 code units.</summary>
    public const int MaximumClusterCharacters = 256;
    /// <summary>Maximum serialized silo address length.</summary>
    public const int MaximumAddressCharacters = 256;
    /// <summary>Maximum public-safe error detail length.</summary>
    public const int MaximumDetailCharacters = 4_096;
    /// <summary>Maximum discovery response byte count.</summary>
    public const int MaximumDiscoveryBytes = 16_384;
    /// <summary>Maximum replica JSON metadata allowance above the append budget.</summary>
    public const int MaximumMetadataBytes = 65_536;
    /// <summary>Bounded Orleans envelope allowance above the exact payload byte count.</summary>
    public const int MaximumEnvelopeOverheadBytes = MaximumMetadataBytes;
    /// <summary>Default per-voter consensus and control nonce capacity.</summary>
    public const int DefaultCriticalReplayCapacity = 16_384;
    /// <summary>Default per-voter application forwarding nonce capacity.</summary>
    public const int DefaultForwardReplayCapacity = 32_768;
    /// <summary>Default per-voter application read-barrier nonce capacity.</summary>
    public const int DefaultReadBarrierReplayCapacity = 16_384;
    /// <summary>Default per-voter data-append nonce capacity.</summary>
    public const int DefaultDataAppendReplayCapacity = 32_768;
    /// <summary>Absolute aggregate retained nonce ceiling across all fixed voters and pools.</summary>
    public const int MaximumRetainedReplayNonces = 1_048_576;
    /// <summary>Initial RPC plus one generation rediscovery within one overall deadline.</summary>
    public const int MaximumAttempts = 2;
    /// <summary>MAC encoding for a successful response.</summary>
    public const int NoError = -1;
    /// <summary>Stable Orleans request type identity.</summary>
    public const string RequestAlias = "keyload.replica.request.v1";
    /// <summary>Stable Orleans reply type identity.</summary>
    public const string ReplyAlias = "keyload.replica.reply.v1";
    /// <summary>Stable replica service identity.</summary>
    public const string ServiceAlias = "keyload.replica.service.v1";
    /// <summary>Stable RPC method identity.</summary>
    public const string ExchangeAlias = "keyload.replica.exchange.v1";
    /// <summary>Request HMAC domain separator.</summary>
    public const string RequestPurpose = "keyload-replica-request-v1";
    /// <summary>Reply HMAC domain separator.</summary>
    public const string ReplyPurpose = "keyload-replica-reply-v1";
    /// <summary>Discovery response HMAC domain separator.</summary>
    public const string DiscoveryPurpose = "keyload-replica-discovery-v1";
    /// <summary>Exact discovery response signature header.</summary>
    public const string DiscoverySignatureHeader = "X-KeyLoad-Discovery-Signature";
    /// <summary>Nonce set by the shared signed HTTP handler.</summary>
    public const string HttpNonceHeader = "X-KeyLoad-Nonce";
    /// <summary>Canonical HTTP request nonce representation.</summary>
    public const string NonceFormat = "N";
    /// <summary>Replica-only JSON candidate identity field.</summary>
    public const string CandidateIdField = "candidateId";
    /// <summary>Replica-only JSON leader identity field.</summary>
    public const string LeaderIdField = "leaderId";
    /// <summary>Invalid replay configuration failure detail.</summary>
    public const string InvalidReplayLimits = "Replica replay pools must be positive and fit the fixed-voter memory budget.";
    /// <summary>Authenticated capacity failure detail, distinct from nonce reuse.</summary>
    public const string ReplayCapacityExceeded = "The authenticated replica replay admission pool is exhausted. Retry with a fresh envelope.";
    /// <summary>Configuration validation failure detail.</summary>
    public const string InvalidOptions = "Replica peer identities, credentials and discovery limits are invalid.";
    /// <summary>Unavailable or unauthenticated discovery failure detail.</summary>
    public const string InvalidDiscovery = "Replica discovery returned an invalid, unauthenticated or unavailable runtime address.";
    /// <summary>Payload budget failure detail.</summary>
    public const string PayloadExceeded = "The replica payload exceeds its transport byte budget.";
    /// <summary>Malformed exact-byte protocol failure detail.</summary>
    public const string InvalidPayload = "The replica payload is not a valid bounded protocol request.";
    /// <summary>Authenticated sender and embedded identity mismatch detail.</summary>
    public const string SenderMismatch = "The replica command identity does not match its authenticated sender.";
    /// <summary>Safe unexpected endpoint error detail.</summary>
    public const string EndpointFailure = "The replica endpoint failed before a verified response was available.";
    /// <summary>Unexpected endpoint error diagnostic template.</summary>
    public const string EndpointFailureLog = "Replica RPC {Method} failed.";
    /// <summary>Interrupted transport failure detail preserving stable-ID retry guidance.</summary>
    public const string TransportUnavailable = "The Orleans replica transport is unavailable. Retry the same command ID.";
    /// <summary>Lifecycle observer diagnostic identity.</summary>
    public const string LifecycleName = "KeyLoadReplicaTransport";
    /// <summary>Maximum accepted peer clock skew and envelope lifetime.</summary>
    public static TimeSpan EnvelopeLifetime { get; } = TimeSpan.FromSeconds(30);
    /// <summary>Default bounded HTTP discovery connection timeout.</summary>
    public static TimeSpan DefaultConnectTimeout { get; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Exact UTF8 without a BOM, rejecting malformed characters and bytes.</summary>
    public static UTF8Encoding Utf8 { get; } = new(false, true);
}
