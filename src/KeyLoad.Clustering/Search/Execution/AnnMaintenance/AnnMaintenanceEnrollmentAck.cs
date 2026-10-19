namespace KeyLoad.Clustering;

internal static class AnnMaintenanceEnrollmentAck
{
    internal static async Task CommitAsync(AnnMaintenanceChildCalls children, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, AnnMaintenanceCapabilityResult state, CancellationToken token)
    {
        if (state.EnrollmentSettlementIntent is { } original)
        {
            _ = await children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(
                OperationKind.CommitProjectionBatch, original, original.CommandId, token).ConfigureAwait(true);
            return;
        }
        if (configured.InitialAnnMaintenance is { } reserved
            && OrleansSerialization.Serialize(reserved).AsSpan().SequenceEqual(OrleansSerialization.Serialize(request)))
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
