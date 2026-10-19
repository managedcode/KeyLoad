namespace KeyLoad.Clustering;

internal static class TextMaintenanceChildScope
{
    internal const string Purpose = "keyload-text-maintenance-child";

    internal static bool IsSelected(GrainRequestEnvelope request)
        => request.Purpose == Purpose && (request.ReadKind is (GrainReadKind.TextMaintenance
                or GrainReadKind.ProjectionBatch or GrainReadKind.OutboxStatus)
                && request.CommandKind is null && request.CommandId == Guid.Empty
            || request.CommandKind is (OperationKind.ConfigureProjectionConsumer
                or OperationKind.CommitProjectionBatch or OperationKind.ReleaseProjectionConsumer)
                && request.ReadKind is null && request.CommandId != Guid.Empty);

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != Purpose && request.ReadKind != GrainReadKind.TextMaintenance)
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
