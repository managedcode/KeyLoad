namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePlacementIntentPhase(Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
        => Issue(PlacementIntentRequestEnvelopeFactory.Create(database, clock, settings.RequestLifetime,
            requestId, operation, originalExpiry));
}
