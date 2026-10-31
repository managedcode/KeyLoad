namespace KeyLoad.Clustering;

/// <summary>Constructs owned request envelopes; the codec validates each result.</summary>
internal static class GrainRequestEnvelopeConstruction
{
    internal static GrainRequestEnvelope Read(Guid requestId, Guid incarnation, string? principalId,
        GrainReadKind kind, object payload)
    {
        if (AnnProjectionObservationScope.Handles(kind))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = payload
        };
    }

    internal static GrainRequestEnvelope Command(Guid requestId, Guid incarnation, string principalId,
        OperationKind kind, Guid commandId, object payload)
    {
        return new()
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = incarnation,
            PrincipalId = principalId,
            CommandKind = kind,
            CommandId = commandId,
            Payload = payload
        };
    }
}
