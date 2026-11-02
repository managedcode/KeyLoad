namespace KeyLoad.Clustering;

/// <summary>Constructs owned request envelopes; the codec validates each result.</summary>
internal static class GrainRequestEnvelopeConstruction
{
    internal static GrainRequestEnvelope Read<TPayload>(Guid requestId, Guid incarnation, string? principalId,
        GrainReadKind kind, TPayload payload) where TPayload : notnull
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
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (payload /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        };
    }

    internal static GrainRequestEnvelope Command<TPayload>(Guid requestId, Guid incarnation, string principalId,
        OperationKind kind, Guid commandId, TPayload payload) where TPayload : notnull
    {
        return new()
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = incarnation,
            PrincipalId = principalId,
            CommandKind = kind,
            CommandId = commandId,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (payload /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        };
    }
}
