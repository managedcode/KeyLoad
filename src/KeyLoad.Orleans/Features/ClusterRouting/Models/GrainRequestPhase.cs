namespace KeyLoad.Orleans;

/// <summary>Closed internal execution phases observable on an admitted native request.</summary>
internal enum GrainRequestPhase
{
    RequestStarted,
    AuthorizationReload,
    BeforeSubmit,
    SubmitReturned,
    ControlledDocumentGrantSettled,
    ControlledDocumentOutcomeReturned
}

/// <summary>Minimal verified request identity for private phase selection.</summary>
internal readonly record struct GrainRequestProbeIdentity(
    Guid RequestId,
    Guid CommandId,
    string? PrincipalId,
    GrainReadKind? ReadKind,
    OperationKind? CommandKind)
{
    internal static GrainRequestProbeIdentity From(GrainRequestEnvelope envelope)
        => new(envelope.RequestId, envelope.CommandId, envelope.PrincipalId,
            envelope.ReadKind, envelope.CommandKind);
}
