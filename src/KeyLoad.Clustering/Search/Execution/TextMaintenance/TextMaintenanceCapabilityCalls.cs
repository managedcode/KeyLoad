namespace KeyLoad.Clustering;

/// <summary>Text maintenance capability requests issued through the parent's maintenance children.</summary>
internal static class TextMaintenanceCapabilityCalls
{
    internal static Task<TextMaintenanceCapabilityResult> CapabilityAsync(this MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityKind kind, CancellationToken token)
        => children.CapabilityAsync(request, kind, null, null, null, token);

    internal static Task<TextMaintenanceCapabilityResult> CapabilityAsync(this MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityKind kind, ProjectionBatch? page,
        CommitProjectionBatchRequest? intent, ProjectionBatchResult? acknowledged, CancellationToken token)
        => children.ReadAsync<TextMaintenanceCapabilityRequest, TextMaintenanceCapabilityResult>(GrainReadKind.TextMaintenance,
            new(children.SessionId, request, kind, page, intent, acknowledged), token);
}
