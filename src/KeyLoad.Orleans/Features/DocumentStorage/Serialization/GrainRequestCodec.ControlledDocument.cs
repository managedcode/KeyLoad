namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateControlledDocumentRead(Guid requestId, string localPrincipalId,
        ControlledDocumentReadRequest request)
        => Issue(GrainRequestEnvelopeConstruction.MovementRead(database, clock.GetUtcNow(), settings.RequestLifetime,
            requestId, localPrincipalId, GrainReadKind.ControlledDocument, ControlledDocumentReadProtocol.Purpose,
            NativeSerialization.Serialize(request), request.Frame.ExpiresAt));
}
