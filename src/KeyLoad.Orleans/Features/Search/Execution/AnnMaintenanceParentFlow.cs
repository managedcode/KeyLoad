using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class AnnMaintenanceParentFlow
{
    private const long EmptySequence = 0;
    private const int ReservedFrames = 12;
    private const int EmptyCount = 0;
    private static readonly string[] MutationKinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument,
        MutationDiscriminatorNames.PutVector, MutationDiscriminatorNames.ApplyVectorProjection];

    internal static async Task<AnnMaintenanceResult> RunAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        AnnMaintenanceOptions options, GrainRoutingOptions routing)
    {
        var token = writer.CancellationToken;
        if (!options.IsValid())
        { throw Errors.Fail(ErrorCode.Validation, AnnMaintenanceOptions.ValidationMessage); }
        if (request.Mode == AnnMaintenanceMode.Release)
        { return await ReleaseAsync(children, request, token).ConfigureAwait(true); }
        var initial = await AnnMaintenanceInitialEnrollment.ReadAsync(children, request, token).ConfigureAwait(true);
        var configureId = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Configure, EmptySequence);
        var configured = await children.CommandAsync<ConfigureProjectionConsumerRequest, ProjectionConsumerInfo>(OperationKind.ConfigureProjectionConsumer,
            new(configureId, request.Consumer, new(request.IndexGeneration, [], [.. MutationKinds])
            { AnnMaintenance = request.MaintenanceProfile })
            { InitialAnnMaintenance = initial }, configureId, token).ConfigureAwait(true);
        var originalState = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.AdmitOriginal,
            token, originalConfigure: configured).ConfigureAwait(true);
        if (originalState.OriginalResult is { } retained)
        { return await ReplayOriginalAsync(children, request, configured, originalState, retained, writer).ConfigureAwait(true); }
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Configure).ConfigureAwait(true);
        var state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Begin, token).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Capture).ConfigureAwait(true);
        ProjectionBatchResult? checkpoint = null;
        if (request.Mode == AnnMaintenanceMode.Restore)
        {
            if (state.OriginalCheckpointIntent is { } original)
            { checkpoint = await CommitAsync(children, original, token).ConfigureAwait(true); }
            state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Load, token).ConfigureAwait(true);
        }
        var maximumPages = Math.Min(options.MaximumReplayPages, routing.MaximumTotalFrames - ReservedFrames);
        if (maximumPages <= EmptyCount)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded); }
        var replayed = await ReplayAsync(children, request, state, checkpoint, maximumPages, writer).ConfigureAwait(true);
        _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Verify, token).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Verify).ConfigureAwait(true);
        state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Publish, token,
            intent: replayed.EmptyIntent).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.NativeIndex).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Publish).ConfigureAwait(true);
        if (replayed.EmptyIntent is { } empty)
        { checkpoint = await CommitAsync(children, empty, token).ConfigureAwait(true); }
        else
        { checkpoint = replayed.Checkpoint; }
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Checkpoint).ConfigureAwait(true);
        var result = new AnnMaintenanceResult(request.CommandId, request.Consumer, request.IndexGeneration,
            AnnMaintenancePhase.Completed, state.Source, state.Count, state.IndexSha256, checkpoint);
        return await CompleteAsync(children, request, configured, result, writer).ConfigureAwait(true);
    }

    private static async Task<AnnMaintenanceResult> ReplayOriginalAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, ProjectionConsumerInfo configured, AnnMaintenanceCapabilityResult state,
        AnnMaintenanceResult retained, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        await AnnMaintenanceEnrollmentAck.CommitAsync(children, request, configured, state, writer.CancellationToken).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Completed).ConfigureAwait(true);
        return retained;
    }

    private static async Task<AnnMaintenanceResult> CompleteAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, ProjectionConsumerInfo configured, AnnMaintenanceResult result,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var settled = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.SettleOriginalResult,
            writer.CancellationToken, fullResult: result).ConfigureAwait(true);
        await AnnMaintenanceEnrollmentAck.CommitAsync(children, request, configured, settled, writer.CancellationToken).ConfigureAwait(true);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Completed).ConfigureAwait(true);
        return result;
    }

    private static async Task<AnnMaintenanceReplayResult> ReplayAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, AnnMaintenanceCapabilityResult state, ProjectionBatchResult? checkpoint,
        int maximumPages, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        var upper = state.Source?.ThroughSequence ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        for (var pageNumber = EmptyCount; pageNumber < maximumPages; pageNumber++)
        {
            var page = await children.ReadAsync<ReadProjectionBatchRequest, ProjectionBatch>(GrainReadKind.ProjectionBatch,
                new(request.Consumer, children.Database.Limits.MaxResults, children.Database.Limits.MaxProjectionBatchBytes, upper), token).ConfigureAwait(true);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                { throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest); }
                var intent = request.Mode == AnnMaintenanceMode.Build || checkpoint is null
                    ? new CommitProjectionBatchRequest(AnnMaintenanceChildIdentity.Create(request.CommandId,
                        AnnMaintenancePhase.Publish, page.ThroughSequence), request.Consumer, page.Token, []) : null;
                return new(checkpoint, intent);
            }
            var id = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Checkpoint, page.ThroughSequence);
            var commit = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.ApplyPage, token, page).ConfigureAwait(true);
            _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.StagePage, token, intent: commit).ConfigureAwait(true);
            checkpoint = await CommitAsync(children, commit, token).ConfigureAwait(true);
            await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Replay).ConfigureAwait(true);
            if (!page.HasMore)
            { return new(checkpoint, null); }
        }
        throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
    }

    private static async Task<AnnMaintenanceResult> ReleaseAsync(AnnMaintenanceChildCalls children,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        var id = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Release, EmptySequence);
        _ = await children.CommandAsync<ReleaseProjectionConsumerRequest, ProjectionConsumerInfo>(OperationKind.ReleaseProjectionConsumer,
            new(id, request.Consumer, request.IndexGeneration), id, token).ConfigureAwait(true);
        _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Release, token).ConfigureAwait(true);
        return new(request.CommandId, request.Consumer, request.IndexGeneration, AnnMaintenancePhase.Completed, null, EmptyCount, null, null);
    }

    private static Task<ProjectionBatchResult> CommitAsync(AnnMaintenanceChildCalls children,
        CommitProjectionBatchRequest intent, CancellationToken token)
        => children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            intent, intent.CommandId, token);

}

internal sealed record AnnMaintenanceReplayResult(ProjectionBatchResult? Checkpoint, CommitProjectionBatchRequest? EmptyIntent);
