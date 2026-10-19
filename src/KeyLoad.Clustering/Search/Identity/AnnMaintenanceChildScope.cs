namespace KeyLoad.Clustering;

internal static class AnnMaintenanceChildScope
{
    internal const string Purpose = "keyload-ann-maintenance-child";

    internal static bool IsSelected(GrainRequestEnvelope request)
        => request.Purpose == Purpose && (request.ReadKind is (GrainReadKind.AnnMaintenance
                or GrainReadKind.ProjectionBatch or GrainReadKind.OutboxStatus)
                && request.CommandKind is null && request.CommandId == Guid.Empty
            || request.CommandKind is (OperationKind.ConfigureProjectionConsumer
                or OperationKind.CommitProjectionBatch or OperationKind.ReleaseProjectionConsumer)
                && request.ReadKind is null && request.CommandId != Guid.Empty);

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != Purpose && request.ReadKind != GrainReadKind.AnnMaintenance)
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
