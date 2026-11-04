using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class AnnMaintenanceParentFlow
{
    private const long EmptySequence = 0;
    private const int EmptyCount = 0;
    private static readonly string[] MutationKinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument,
        MutationDiscriminatorNames.PutVector, MutationDiscriminatorNames.ApplyVectorProjection];

    internal static async Task<AnnMaintenanceResult> RunAsync(MaintenanceChildCalls children,
        AnnMaintenanceRequest request, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        if (request.Mode == AnnMaintenanceMode.Release)
        {
            return await ReleaseAsync(children, request, token);
        }
        var configured = await ConfigureOriginalAsync(children, request, token);
        var originalState = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.AdmitOriginal,
            token, originalConfigure: configured);
        if (originalState.OriginalResult is { } retained)
        {
            return await ReplayOriginalAsync(children, request, configured, originalState, retained, writer);
        }
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Configure);
        var state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Begin, token);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Capture);
        ProjectionBatchResult? checkpoint = null;
        if (request.Mode == AnnMaintenanceMode.Restore)
        {
            if (state.OriginalCheckpointIntent is { } original)
            {
                checkpoint = await CommitAsync(children, original, token);
            }
            state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Load, token);
        }
        var replayed = await ReplayAsync(children, request, state, checkpoint, writer);
        _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Verify, token);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Verify);
        state = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Publish, token,
            intent: replayed.EmptyIntent);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.RuntimeIndex);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Publish);
        if (replayed.EmptyIntent is { } empty)
        {
            checkpoint = await CommitAsync(children, empty, token);
        }
        else
        {
            checkpoint = replayed.Checkpoint;
        }
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Checkpoint);
        var result = new AnnMaintenanceResult(request.CommandId, request.Consumer, request.IndexGeneration,
            AnnMaintenancePhase.Completed, state.Source, state.Count, state.IndexSha256, checkpoint);
        return await CompleteAsync(children, request, configured, result, writer);
    }

    private static async Task<ProjectionConsumerInfo> ConfigureOriginalAsync(MaintenanceChildCalls children, AnnMaintenanceRequest request, CancellationToken token)
    {
        var initial = (await AnnMaintenanceInitialEnrollment.ReadAsync(children, request, token)).Value;
        var configureId = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Configure, EmptySequence);
        var configured = await children.CommandAsync<ConfigureProjectionConsumerRequest, ProjectionConsumerInfo>(OperationKind.ConfigureProjectionConsumer,
            new(configureId, request.Consumer, new(request.IndexGeneration, [], [.. MutationKinds])
            {
                AnnMaintenance = request.MaintenanceProfile
            })
            {
                InitialAnnMaintenance = initial
            }, configureId, token);
        return configured;
    }

    private static async Task<AnnMaintenanceResult> ReplayOriginalAsync(MaintenanceChildCalls children,
        AnnMaintenanceRequest request, ProjectionConsumerInfo configured, AnnMaintenanceCapabilityResult state,
        AnnMaintenanceResult retained, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        await AnnMaintenanceEnrollmentAck.CommitAsync(children, request, configured, state, writer.CancellationToken);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Completed);
        return retained;
    }

    private static async Task<AnnMaintenanceResult> CompleteAsync(MaintenanceChildCalls children,
        AnnMaintenanceRequest request, ProjectionConsumerInfo configured, AnnMaintenanceResult result,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var settled = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.SettleOriginalResult,
            writer.CancellationToken, fullResult: result);
        await AnnMaintenanceEnrollmentAck.CommitAsync(children, request, configured, settled, writer.CancellationToken);
        await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Completed);
        return result;
    }

    private static async Task<AnnMaintenanceReplayResult> ReplayAsync(MaintenanceChildCalls children,
        AnnMaintenanceRequest request, AnnMaintenanceCapabilityResult state, ProjectionBatchResult? checkpoint,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        var upper = state.Source?.ThroughSequence ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        while (true)
        {
            var page = await children.ReadAsync<ReadProjectionBatchRequest, ProjectionBatch>(GrainReadKind.ProjectionBatch,
                new(request.Consumer, ProjectionReplayPage.Records, upper), token);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
                }
                var intent = request.Mode == AnnMaintenanceMode.Build || checkpoint is null
                    ? new CommitProjectionBatchRequest(AnnMaintenanceChildIdentity.Create(request.CommandId,
                        AnnMaintenancePhase.Publish, page.ThroughSequence), request.Consumer, page.Token, []) : null;
                return new(checkpoint, intent);
            }
            var id = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Checkpoint, page.ThroughSequence);
            var commit = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.ApplyPage, token, page);
            _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.StagePage, token, intent: commit);
            checkpoint = await CommitAsync(children, commit, token);
            await AnnMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, AnnMaintenancePhase.Replay);
            if (!page.HasMore)
            {
                return new(checkpoint, null);
            }
        }
    }

    private static async Task<AnnMaintenanceResult> ReleaseAsync(MaintenanceChildCalls children,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        var id = AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Release, EmptySequence);
        _ = await children.CommandAsync<ReleaseProjectionConsumerRequest, ProjectionConsumerInfo>(OperationKind.ReleaseProjectionConsumer,
            new(id, request.Consumer, request.IndexGeneration), id, token);
        _ = await children.CapabilityAsync(request, AnnMaintenanceCapabilityKind.Release, token);
        return new(request.CommandId, request.Consumer, request.IndexGeneration, AnnMaintenancePhase.Completed, null, EmptyCount, null, null);
    }

    private static Task<ProjectionBatchResult> CommitAsync(MaintenanceChildCalls children,
        CommitProjectionBatchRequest intent, CancellationToken token)
        => children.CommandAsync<CommitProjectionBatchRequest, ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            intent, intent.CommandId, token);

}
