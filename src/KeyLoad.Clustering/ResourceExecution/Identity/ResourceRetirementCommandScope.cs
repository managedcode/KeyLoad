namespace KeyLoad.Clustering;

internal static class ResourceRetirementCommandScope
{
    internal const string Purpose = "keyload.resource-retirement.command.v1";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var retirement = request.CommandKind == OperationKind.ResourceRetirementPhase;
        if (request.Purpose != Purpose && !retirement)
        {
            return false;
        }
        if (!retirement || request.Purpose != Purpose || request.ReadKind is not null
            || request.CommandId == Guid.Empty || request.Observation is not null
            || !NativeCommandMetadataAdmission.Valid(request.Metadata))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        return true;
    }
}
