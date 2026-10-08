namespace KeyLoad.Orleans;

internal static class PartitionMovementRequestScope
{
    internal const string OutcomePurpose = "keyload-partition-movement-outcome-query-v1";
    internal const string Purpose = "keyload-partition-movement-verified-v1";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var outcome = request.ReadKind == GrainReadKind.PartitionMovementOutcome;
        if (request.Purpose == OutcomePurpose || outcome)
        {
            if (!outcome || request.Purpose != OutcomePurpose || request.CommandKind is not null
                || request.CommandId != Guid.Empty)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
            return true;
        }
        var movement = request.CommandKind == OperationKind.PartitionMovementPhase;
        var capture = request.ReadKind == GrainReadKind.PartitionMovementCapture;
        if (request.Purpose != Purpose)
        {
            if (movement || capture)
            { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
            return false;
        }
        if (!(movement && request.ReadKind is null
            || capture && request.CommandKind is null))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
