namespace KeyLoad.Clustering;

/// <summary>Signed server authority and exact payload for one independently identified Orleans request.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainNativeContracts.EnvelopeAlias)]
public sealed record GrainRequestEnvelope
{
    /// <summary>Versioned purpose preventing cross-protocol token reuse.</summary>
    [Orleans.Id(GrainNativeContracts.PurposeField)]
    public required string Purpose
    {
        get; init;
    }
    /// <summary>Unique operation identity; retries retain the command ID but receive a fresh request ID.</summary>
    [Orleans.Id(GrainNativeContracts.RequestIdField)]
    public required Guid RequestId
    {
        get; init;
    }
    /// <summary>Database incarnation rejecting tokens from another installation.</summary>
    [Orleans.Id(GrainNativeContracts.IncarnationField)]
    public required Guid Incarnation
    {
        get; init;
    }
    /// <summary>Persisted principal identifier, absent only during API-key authentication.</summary>
    [Orleans.Id(GrainNativeContracts.PrincipalIdField)]
    public string? PrincipalId
    {
        get; init;
    }
    /// <summary>Read capability, mutually exclusive with the command kind.</summary>
    [Orleans.Id(GrainNativeContracts.ReadKindField)]
    public GrainReadKind? ReadKind
    {
        get; init;
    }
    /// <summary>Replicated command kind, mutually exclusive with the read capability.</summary>
    [Orleans.Id(GrainNativeContracts.CommandKindField)]
    public OperationKind? CommandKind
    {
        get; init;
    }
    /// <summary>Stable deduplication ID for commands; empty for reads.</summary>
    [Orleans.Id(GrainNativeContracts.CommandIdField)]
    public Guid CommandId
    {
        get; init;
    }
    /// <summary>Exact native typed operation payload.</summary>
    [Orleans.Id(GrainNativeContracts.PayloadField)]
    public required ReadOnlyMemory<byte> Payload
    {
        get; init;
    }
    /// <summary>Bounded server-issued expiration evaluated with the runtime system clock.</summary>
    [Orleans.Id(GrainNativeContracts.ExpiresAtField)]
    public required DateTimeOffset ExpiresAt
    {
        get; init;
    }
    /// <summary>Private retirement command metadata bounds, absent for every other purpose.</summary>
    [Orleans.Id(GrainNativeContracts.MetadataField)]
    internal NativeCommandMetadataAdmissionV1? Metadata
    {
        get; init;
    }
}
