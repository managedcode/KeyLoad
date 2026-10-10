using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class TextMaintenanceReplay
{
    private const int InitialPage = 0;

    internal static async Task<ProjectionBatchResult?> RunAsync(TextMaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult state,
        ProjectionBatchResult? checkpoint, int maximumPages,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        for (var pageNumber = InitialPage; pageNumber < maximumPages; pageNumber++)
        {
            var page = await children.ReadAsync<ReadProjectionBatchRequest, ProjectionBatch>(GrainReadKind.ProjectionBatch,
                new(request.Consumer, children.Database.Limits.MaxResults,
                    children.Database.Limits.MaxProjectionBatchBytes, state.ReplayUpperSequence), token).ConfigureAwait(true);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != state.ReplayUpperSequence || !page.Entries.IsEmpty)
                { throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest); }
                if (state.IndexSha256 is not null && state.ThroughSequence == state.ReplayUpperSequence)
                { return checkpoint ?? await OriginalCheckpointAsync(children, request, state, token).ConfigureAwait(true); }
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
            { return checkpoint; }
        }
        throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
    }

    internal static async Task<TextMaintenanceCommittedPage> ApplyAndCommitAsync(TextMaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, CommitProjectionBatchRequest original,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        _ = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.ApplyIntent, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.NativeIndex)
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
        { throw Errors.Fail(ErrorCode.Corruption, TextIndexMaintenanceProtocol.Interrupted); }
        var original = TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Checkpoint, state.Checkpoint);
        if (configured.Checkpoint < state.Checkpoint
            && state.LastSettledCheckpointRequest?.CommandId != original
            && state.OriginalCheckpointIntent?.CommandId != original)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, TextIndexMaintenanceProtocol.Interrupted); }
    }

    private static Task<ProjectionBatchResult?> OriginalCheckpointAsync(TextMaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, TextMaintenanceCapabilityResult state, CancellationToken token)
    {
        var original = state.LastSettledCheckpointRequest;
        if (original is null || original.CommandId != TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Checkpoint, state.Checkpoint))
        { return Task.FromResult<ProjectionBatchResult?>(null); }
        return ReplayOriginalCheckpointAsync(children, original, token);
    }

    private static async Task<ProjectionBatchResult?> ReplayOriginalCheckpointAsync(TextMaintenanceChildCalls children,
        CommitProjectionBatchRequest original, CancellationToken token)
        => await children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(
            OperationKind.CommitProjectionBatch, original, original.CommandId, token).ConfigureAwait(true);

}

internal sealed record TextMaintenanceCommittedPage(TextMaintenanceCapabilityResult State, ProjectionBatchResult Checkpoint);
