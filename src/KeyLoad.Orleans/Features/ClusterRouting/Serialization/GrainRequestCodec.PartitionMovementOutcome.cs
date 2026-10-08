namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePartitionMovementOutcome(Guid requestId, string localPrincipalId,
        PartitionMovementOutcomeQuery query, DateTimeOffset queryExpiry)
    {
        var now = clock.GetUtcNow();
        if (queryExpiry <= now || queryExpiry > now + settings.RequestLifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return Issue(new GrainRequestEnvelope
        {
            Purpose = PartitionMovementRequestScope.OutcomePurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = localPrincipalId,
            ReadKind = GrainReadKind.PartitionMovementOutcome,
            Payload = Encode(NativeSerialization.Serialize(query)),
            ExpiresAt = queryExpiry
        });
    }
}
