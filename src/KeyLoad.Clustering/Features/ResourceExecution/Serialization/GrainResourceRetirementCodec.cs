using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainResourceRetirementCodec
{
    internal static string CreateResourceRetirementCommand(this GrainRequestCodec codec,
        Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry, NativeCommandMetadataAdmissionV1 metadata)
    {
        var context = codec.LifetimeReadContext;
        NativeCommandMetadataAdmission.Require(metadata, context.Database.Limits);
        if (originalExpiry <= context.Now || originalExpiry > context.Now + context.Lifetime)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementReadScope.InvalidRequest);
        }
        var verified = context.Database.VerifyOperationAuthority(operation);
        if (verified.Kind != OperationKind.ResourceRetirementPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return codec.Issue(new GrainRequestEnvelope
        {
            Purpose = ResourceRetirementCommandScope.Purpose,
            RequestId = requestId,
            Incarnation = context.Database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), context.Database.Limits.MaxBatchBytes),
            ExpiresAt = originalExpiry,
            Metadata = metadata
        });
    }

    internal static string CreateResourceRetirementRead(this GrainRequestCodec codec,
        Guid requestId, string verifiedPrincipalId, ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var context = codec.LifetimeReadContext;
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(context.Database, context.Now,
            context.Lifetime, requestId, verifiedPrincipalId, GrainReadKind.ResourceRetirement, payload, expiresAt);
        return codec.Issue(envelope with
        {
            Purpose = ResourceRetirementReadScope.Purpose
        });
    }
}
