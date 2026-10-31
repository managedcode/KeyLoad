namespace KeyLoad.Clustering;

/// <summary>ANN maintenance capability requests issued through the parent's maintenance children.</summary>
internal static class AnnMaintenanceCapabilityCalls
{
    internal static Task<AnnMaintenanceCapabilityResult> CapabilityAsync(this MaintenanceChildCalls children,
        AnnMaintenanceRequest request, AnnMaintenanceCapabilityKind kind, CancellationToken token, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, AnnMaintenanceResult? fullResult = null,
        ProjectionConsumerInfo? originalConfigure = null)
        => children.ReadAsync<AnnMaintenanceCapabilityRequest, AnnMaintenanceCapabilityResult>(GrainReadKind.AnnMaintenance,
            new(request, children.SessionId, kind, page, intent, fullResult, originalConfigure), token);
}
