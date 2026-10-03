namespace KeyLoad.Orleans;

/// <summary>Signed server authority and exact payload for one independently identified Orleans request.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainNativeContracts.EnvelopeAlias)]
public sealed record GrainRequestEnvelope
{
    /// <summary>Versioned purpose preventing cross-protocol token reuse.</summary>
    [global::Orleans.Id(GrainNativeContracts.PurposeField)] public required string Purpose { get; init; }
    /// <summary>Unique request actor key; retries retain the command ID but receive a fresh request ID.</summary>
    [global::Orleans.Id(GrainNativeContracts.RequestIdField)] public required Guid RequestId { get; init; }
    /// <summary>Database incarnation rejecting tokens from another installation.</summary>
    [global::Orleans.Id(GrainNativeContracts.IncarnationField)] public required Guid Incarnation { get; init; }
    /// <summary>Persisted principal identifier, absent only during API-key authentication.</summary>
    [global::Orleans.Id(GrainNativeContracts.PrincipalIdField)] public string? PrincipalId { get; init; }
    /// <summary>Read capability, mutually exclusive with the command kind.</summary>
    [global::Orleans.Id(GrainNativeContracts.ReadKindField)] public GrainReadKind? ReadKind { get; init; }
    /// <summary>Replicated command kind, mutually exclusive with the read capability.</summary>
    [global::Orleans.Id(GrainNativeContracts.CommandKindField)] public OperationKind? CommandKind { get; init; }
    /// <summary>Stable deduplication ID for commands; empty for reads.</summary>
    [global::Orleans.Id(GrainNativeContracts.CommandIdField)] public Guid CommandId { get; init; }
    /// <summary>Exact native typed operation payload; retired field 7 is never reused.</summary>
    [global::Orleans.Id(GrainNativeContracts.PayloadField)]
    public required ReadOnlyMemory<byte> Payload { get; init; }
    /// <summary>Bounded server-issued expiration evaluated with the runtime system clock.</summary>
    [global::Orleans.Id(GrainNativeContracts.ExpiresAtField)] public required DateTimeOffset ExpiresAt { get; init; }
}
