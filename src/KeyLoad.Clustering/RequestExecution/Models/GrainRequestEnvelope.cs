namespace KeyLoad.Clustering;

/// <summary>Signed server authority and exact payload for one independently identified Orleans request.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainContracts.EnvelopeAlias)]
public sealed record GrainRequestEnvelope
{
    /// <summary>Versioned purpose preventing cross-protocol token reuse.</summary>
    [Orleans.Id(GrainContracts.PurposeField)]
    public required string Purpose
    {
        get; init;
    }
    /// <summary>Unique operation identity; retries retain the command ID but receive a fresh request ID.</summary>
    [Orleans.Id(GrainContracts.RequestIdField)]
    public required Guid RequestId
    {
        get; init;
    }
    /// <summary>Database incarnation rejecting tokens from another installation.</summary>
    [Orleans.Id(GrainContracts.IncarnationField)]
    public required Guid Incarnation
    {
        get; init;
    }
    /// <summary>Persisted principal identifier, absent only during API-key authentication.</summary>
    [Orleans.Id(GrainContracts.PrincipalIdField)]
    public string? PrincipalId
    {
        get; init;
    }
    /// <summary>Read capability, mutually exclusive with the command kind.</summary>
    [Orleans.Id(GrainContracts.ReadKindField)]
    public GrainReadKind? ReadKind
    {
        get; init;
    }
    /// <summary>Replicated command kind, mutually exclusive with the read capability.</summary>
    [Orleans.Id(GrainContracts.CommandKindField)]
    public OperationKind? CommandKind
    {
        get; init;
    }
    /// <summary>Stable deduplication ID for commands; empty for reads.</summary>
    [Orleans.Id(GrainContracts.CommandIdField)]
    public Guid CommandId
    {
        get; init;
    }
    /// <summary>The typed operation input, serialized once by Orleans with the envelope.</summary>
    [Orleans.Id(GrainContracts.PayloadField)]
    public required object Payload
    {
        get; init;
    }
}
