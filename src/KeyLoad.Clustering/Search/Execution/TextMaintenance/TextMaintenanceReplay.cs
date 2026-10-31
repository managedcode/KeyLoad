using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class TextMaintenanceReplay
{
    internal static async Task<ProjectionBatchResult?> RunAsync(MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult state,
        ProjectionBatchResult? checkpoint, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        while (true)
        {
            var page = await children.ReadAsync<ReadProjectionBatchRequest, ProjectionBatch>(GrainReadKind.ProjectionBatch,
                new(request.Consumer, ProjectionReplayPage.Records, state.ReplayUpperSequence), token).ConfigureAwait(true);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != state.ReplayUpperSequence || !page.Entries.IsEmpty)
                {
                    throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
                }
                if (state.IndexSha256 is not null && state.ThroughSequence == state.ReplayUpperSequence)
                {
                    return checkpoint ?? await OriginalCheckpointAsync(children, request, state, token).ConfigureAwait(true);
                }
            }
            var commandId = TextMaintenanceChildIdentity.Create(request.CommandId,
                TextIndexMaintenancePhase.Checkpoint, page.ThroughSequence);
            var original = new CommitProjectionBatchRequest(commandId, request.Consumer, page.Token, []);
            _ = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.PreparePage,
                page, original, null, token).ConfigureAwait(true);
            var completed = await ApplyAndCommitAsync(children, request, original, writer).ConfigureAwait(true);
            state = completed.State;
            checkpoint = completed.Checkpoint;
            if (state.Checkpoint == state.ReplayUpperSequence && state.ThroughSequence == state.ReplayUpperSequence)
            {
                return checkpoint;
            }
        }
    }

    internal static async Task<TextMaintenanceCommittedPage> ApplyAndCommitAsync(MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, CommitProjectionBatchRequest original,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        _ = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.ApplyIntent, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.RuntimeIndex)
            .ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Publish)
            .ConfigureAwait(true);
        var acknowledged = await children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(
            OperationKind.CommitProjectionBatch, original, original.CommandId, token).ConfigureAwait(true);
        var state = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.SettleCheckpoint,
            null, original, acknowledged, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Checkpoint)
            .ConfigureAwait(true);
        return new(state, acknowledged);
    }

    internal static void RequireOriginalParent(TextIndexMaintenanceRequest request,
        ProjectionConsumerInfo configured, TextMaintenanceCapabilityResult state)
    {
        if (configured.Checkpoint > state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, TextIndexMaintenanceProtocol.Interrupted);
        }
        var original = TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Checkpoint, state.Checkpoint);
        if (configured.Checkpoint < state.Checkpoint
            && state.LastSettledCheckpointRequest?.CommandId != original
            && state.OriginalCheckpointIntent?.CommandId != original)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, TextIndexMaintenanceProtocol.Interrupted);
        }
    }

    private static Task<ProjectionBatchResult?> OriginalCheckpointAsync(MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult state, CancellationToken token)
    {
        var original = state.LastSettledCheckpointRequest;
        if (original is null || original.CommandId != TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Checkpoint, state.Checkpoint))
        {
            return Task.FromResult<ProjectionBatchResult?>(null);
        }
        return ReplayOriginalCheckpointAsync(children, original, token);
    }

    private static async Task<ProjectionBatchResult?> ReplayOriginalCheckpointAsync(MaintenanceChildCalls children,
        CommitProjectionBatchRequest original, CancellationToken token)
        => await children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(
            OperationKind.CommitProjectionBatch, original, original.CommandId, token).ConfigureAwait(true);

}
