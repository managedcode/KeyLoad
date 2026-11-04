namespace KeyLoad.Clustering;

internal static class TextMaintenanceRelease
{
    private const long InitialSequence = 0;
    private const int EmptyRecords = 0;

    internal static async Task<TextIndexMaintenanceResult> RunAsync(MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var commandId = TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Release, InitialSequence);
        var actual = await children.CommandAsync<ReleaseProjectionConsumerRequest, ProjectionConsumerInfo>(
            OperationKind.ReleaseProjectionConsumer, new(commandId, request.Consumer, request.IndexGeneration),
            commandId, token);
        _ = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.Release, token);
        return new(request.CommandId, request.Consumer, request.IndexGeneration, TextIndexMaintenancePhase.Completed,
            null, EmptyRecords, null, null, actual);
    }
}
