using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Constructs owned request envelopes; the existing codec still validates and signs each result.</summary>
internal static class GrainRequestEnvelopeConstruction
{
    internal static GrainRequestEnvelope Read(Guid requestId, Guid incarnation, string? principalId,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
    {
        if (kind == GrainReadKind.OwnerCapacity || ResourceOwnerInventoryReadScope.Handles(kind)
            || ResourceRetirementReadScope.Handles(kind)
            || EventVectorRequestScope.HandlesRead(kind) || AnnProjectionObservationScope.Handles(kind)
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
        if (kind is OperationKind.PlacementIntentPhase or OperationKind.ResourceRetirementPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
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

    internal static GrainRequestEnvelope MovementRead(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind,
        string purpose, byte[] payload, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + requestLifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return new GrainRequestEnvelope
        {
            Purpose = purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainNativePayload.Copy(payload, database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }
}
