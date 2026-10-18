namespace KeyLoad.Clustering;

/// <summary>Constructs owned request envelopes; the existing codec still validates and signs each result.</summary>
internal static class GrainRequestEnvelopeConstruction
{
    internal static GrainRequestEnvelope Read(Guid requestId, Guid incarnation, string? principalId,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
    {
        if (AnnProjectionObservationScope.Handles(kind)
            || AnnProjectionLifetimeObservationScope.Handles(kind))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = payload,
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope Command(Guid requestId, Guid incarnation, string principalId,
        OperationKind kind, Guid commandId, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
    {
        return new()
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = incarnation,
            PrincipalId = principalId,
            CommandKind = kind,
            CommandId = commandId,
            Payload = payload,
            ExpiresAt = expiry
        };
    }

}
