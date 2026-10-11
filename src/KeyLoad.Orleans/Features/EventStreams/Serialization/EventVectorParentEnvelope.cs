using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class EventVectorParentEnvelope
{
    private const int Version = 1;

    internal static GrainRequestEnvelope Create(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, string principalId, EventFeedControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        if (requestId == Guid.Empty || request.CommandId == Guid.Empty || request.Version != Version
            || request.ControlPartition is null || request.Scope is null || string.IsNullOrWhiteSpace(principalId))
        { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
        DatabaseEngine.ValidatePartition(request.ControlPartition);
        var encoded = GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes);
        var envelope = GrainRequestEnvelopeConstruction.Command(requestId, database.Store.Identity.Incarnation,
            principalId, OperationKind.EventFeedControl, request.CommandId, encoded, now + lifetime);
        return envelope with { Purpose = EventVectorRequestScope.ParentPurpose };
    }
}
