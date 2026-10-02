using System.Text.Json.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Signed server authority and exact payload for one independently identified Orleans request.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainRoutingProtocol.EnvelopeAlias)]
public sealed record GrainRequestEnvelope
{
    /// <summary>Versioned purpose preventing cross-protocol token reuse.</summary>
    [global::Orleans.Id(0)] public required string Purpose { get; init; }
    /// <summary>Unique request actor key; retries retain the command ID but receive a fresh request ID.</summary>
    [global::Orleans.Id(1)] public required Guid RequestId { get; init; }
    /// <summary>Database incarnation rejecting tokens from another installation.</summary>
    [global::Orleans.Id(2)] public required Guid Incarnation { get; init; }
    /// <summary>Persisted principal identifier, absent only during API-key authentication.</summary>
    [global::Orleans.Id(3)] public string? PrincipalId { get; init; }
    /// <summary>Read capability, mutually exclusive with the command kind.</summary>
    [global::Orleans.Id(4)] public GrainReadKind? ReadKind { get; init; }
    /// <summary>Replicated command kind, mutually exclusive with the read capability.</summary>
    [global::Orleans.Id(5)] public OperationKind? CommandKind { get; init; }
    /// <summary>Stable deduplication ID for commands; empty for reads.</summary>
    [global::Orleans.Id(6)] public Guid CommandId { get; init; }
    /// <summary>Base64url of the exact UTF8 JSON payload, avoiding recursive escaping.</summary>
    [global::Orleans.Id(7), JsonPropertyName(GrainRoutingProtocol.PayloadWireName)]
    public required string EncodedPayload { get; init; }
    /// <summary>Bounded server-issued expiration evaluated with the runtime system clock.</summary>
    [global::Orleans.Id(8)] public required DateTimeOffset ExpiresAt { get; init; }
}
