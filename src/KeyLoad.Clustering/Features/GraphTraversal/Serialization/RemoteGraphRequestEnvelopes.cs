using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class RemoteGraphRequestEnvelopes
{
    internal static GrainRequestEnvelope Read(DatabaseEngine database, TimeProvider clock, TimeSpan lifetime,
        Guid requestId, string principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
    {
        if (!RemoteGraphRequestScope.HandlesRead(kind))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return GrainRequestCapabilityEnvelopes.RemoteRead(database, clock.GetUtcNow(), lifetime,
            requestId, principalId, kind, payload, expiry) with
        {
            Purpose = RemoteGraphRequestScope.ReadPurpose
        };
    }

    internal static GrainRequestEnvelope Phase(DatabaseEngine database, DateTimeOffset now, TimeSpan lifetime,
        Guid requestId, ReplicatedOperation operation, DateTimeOffset expiry)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(operation);
        OnlineTextRequestEnvelopes.RequireExpiry(now, lifetime, expiry);
        var verified = database.VerifyOperationAuthority(operation);
        var purpose = RemoteGraphRequestScope.PhasePurpose(verified.Kind)
            ?? throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        return new()
        {
            Purpose = purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope ParentCommand(DatabaseEngine database, DateTimeOffset now, TimeSpan lifetime,
        Guid requestId, string principalId, OperationKind kind, Guid commandId, ReadOnlyMemory<byte> payload,
        DateTimeOffset expiry)
    {
        if (!RemoteGraphRequestScope.HandlesParent(kind))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        OnlineTextRequestEnvelopes.RequireExpiry(now, lifetime, expiry);
        return new()
        {
            Purpose = RemoteGraphRequestScope.ParentPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            CommandKind = kind,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope ParentRead(DatabaseEngine database, DateTimeOffset now, TimeSpan lifetime,
        Guid requestId, string principalId, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
        => GrainRequestCapabilityEnvelopes.RemoteRead(database, now, lifetime, requestId, principalId,
            GrainReadKind.RemoteGraphIncomingParent, payload, expiry) with
        {
            Purpose = RemoteGraphRequestScope.ParentPurpose
        };
}
