namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePartitionMovementCapture(Guid requestId, string localPrincipalId,
        PartitionMovementCaptureCapability capability)
        => Issue(PartitionMovementRequestEnvelopes.Capture(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, localPrincipalId, capability));
}
