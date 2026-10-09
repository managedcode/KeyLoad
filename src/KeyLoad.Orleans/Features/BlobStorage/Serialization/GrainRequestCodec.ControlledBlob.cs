namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateControlledBlobRead(Guid requestId, string localPrincipalId, ControlledBlobReadRequest request)
        => Issue(GrainRequestEnvelopeConstruction.MovementRead(database, clock.GetUtcNow(), settings.RequestLifetime,
            requestId, localPrincipalId, GrainReadKind.ControlledBlob, ControlledBlobReadProtocol.Purpose,
            NativeSerialization.Serialize(request), request.Frame.ExpiresAt));
}
