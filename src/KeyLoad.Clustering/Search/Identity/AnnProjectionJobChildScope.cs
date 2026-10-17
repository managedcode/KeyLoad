namespace KeyLoad.Clustering;

internal static class AnnProjectionJobChildScope
{
    internal const string Purpose = "keyload-ann-projection-job-child-v1";

    internal static bool IsSelected(GrainRequestEnvelope request)
        => request.Purpose == Purpose && request.ReadKind is null
            && request.CommandKind == OperationKind.MaintainAnnIndex && request.CommandId != Guid.Empty;

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != Purpose)
        {
            return false;
        }
        if (!IsSelected(request))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return true;
    }
}
