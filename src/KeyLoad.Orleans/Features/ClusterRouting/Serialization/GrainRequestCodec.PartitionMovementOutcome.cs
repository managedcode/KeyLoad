namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePartitionMovementOutcome(Guid requestId, string localPrincipalId,
        PartitionMovementOutcomeQuery query, DateTimeOffset queryExpiry)
        => Issue(PartitionMovementRequestEnvelopes.Outcome(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, localPrincipalId, query, queryExpiry));
}
