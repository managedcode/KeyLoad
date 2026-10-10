namespace KeyLoad.Orleans;

internal static class EventVectorRequestScope
{
    internal const string ControlPurpose = "keyload-event-vector-control-phase-v1";
    internal const string SourcePurpose = "keyload-event-vector-source-phase-v1";

    internal static bool HandlesRead(GrainReadKind? kind) => kind is GrainReadKind.EventVectorCoverage
        or GrainReadKind.EventVectorOriginalOutcome or GrainReadKind.EventVectorSources;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var read = HandlesRead(request.ReadKind);
        if (!read && request.Purpose != SourcePurpose && request.Purpose != ControlPurpose)
        { return false; }
        if (!read || request.Purpose != SourcePurpose || request.CommandKind is not null
            || request.CommandId != Guid.Empty)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return true;
    }
}
