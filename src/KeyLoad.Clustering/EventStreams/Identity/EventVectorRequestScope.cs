namespace KeyLoad.Clustering;

internal static class EventVectorRequestScope
{
    internal const string ParentPurpose = "keyload-event-vector-parent-v1";
    internal const string ControlPurpose = "keyload-event-vector-control-phase-v1";
    internal const string SourcePurpose = "keyload-event-vector-source-phase-v1";

    internal static bool HandlesRead(GrainReadKind? kind) => kind is GrainReadKind.EventVectorCoverage
        or GrainReadKind.EventVectorOriginalOutcome or GrainReadKind.EventVectorSources;

    internal static bool IsReceivingOnly(DecodedGrainRequest request)
        => request.Envelope.CommandKind == OperationKind.EventFeedControl
            && request.Envelope.Purpose == ParentPurpose
            && GrainNativePayload.ReadCommand<EventFeedControlRequest>(request.Payload).Action == EventFeedControlAction.Tail;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose == ParentPurpose)
        {
            if (request.CommandKind != OperationKind.EventFeedControl || request.ReadKind is not null
                || request.CommandId == Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return true;
        }
        var source = request.CommandKind == OperationKind.EventFeedSourcePhase;
        var control = request.CommandKind == OperationKind.EventFeedControl;
        var read = HandlesRead(request.ReadKind);
        if (request.Purpose != SourcePurpose && request.Purpose != ControlPurpose && !source && !control && !read)
        {
            return false;
        }
        if (request.Purpose != (control ? ControlPurpose : SourcePurpose))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        if (!((source || control) && request.ReadKind is null || read && request.CommandKind is null
                && request.CommandId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
