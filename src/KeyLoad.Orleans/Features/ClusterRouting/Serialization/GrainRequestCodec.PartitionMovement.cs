namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePartitionMovementCommand(Guid requestId, ReplicatedOperation operation,
        DateTimeOffset originalExpiry)
        => Issue(PartitionMovementRequestEnvelopes.Command(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, operation, originalExpiry));
}
