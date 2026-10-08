namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateControlledDocumentRead(Guid requestId, string localPrincipalId,
        ControlledDocumentReadRequest request)
    {
        var expiresAt = request.Frame.ExpiresAt;
        var now = clock.GetUtcNow();
        if (expiresAt <= now || expiresAt > now + settings.RequestLifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return Issue(new GrainRequestEnvelope
        {
            Purpose = ControlledDocumentReadProtocol.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = localPrincipalId,
            ReadKind = GrainReadKind.ControlledDocument,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes),
            ExpiresAt = expiresAt
        });
    }
}
