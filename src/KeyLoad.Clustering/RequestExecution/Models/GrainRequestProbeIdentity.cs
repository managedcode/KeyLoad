namespace KeyLoad.Clustering;

/// <summary>Minimal verified request identity for private phase selection.</summary>
internal readonly record struct GrainRequestProbeIdentity(
    Guid RequestId,
    Guid CommandId,
    string? PrincipalId,
    GrainReadKind? ReadKind,
    OperationKind? CommandKind)
{
    internal DateTimeOffset OriginalExpiresAt
    {
        get; init;
    }

    internal static GrainRequestProbeIdentity From(GrainRequestEnvelope envelope)
        => new(envelope.RequestId, envelope.CommandId, envelope.PrincipalId,
            envelope.ReadKind, envelope.CommandKind)
        {
            OriginalExpiresAt = envelope.ExpiresAt
        };
}
