using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class TextMaintenanceParentFlow
{
    private const long InitialSequence = 0;
    private const int ReservedFrames = 8;
    private const int FramesPerPage = 3;
    private const int EmptyPages = 0;
    private static readonly string[] MutationKinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument];

    internal static async Task<TextIndexMaintenanceResult> RunAsync(TextMaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        TextIndexMaintenanceOptions options, GrainRoutingOptions routing)
    {
        var token = writer.CancellationToken;
        if (!options.IsValid())
        { throw Errors.Fail(ErrorCode.Validation, TextIndexMaintenanceOptions.ValidationMessage); }
        if (request.Mode == TextIndexMaintenanceMode.Release)
        { return await TextMaintenanceRelease.RunAsync(children, request, token).ConfigureAwait(true); }
        var configureId = TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Configure, InitialSequence);
        _ = await children.CommandAsync<ConfigureProjectionConsumerRequest, ProjectionConsumerInfo>(
            OperationKind.ConfigureProjectionConsumer,
            new(configureId, request.Consumer, new(request.IndexGeneration, [request.Collection], [.. MutationKinds])),
            configureId, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Configure)
            .ConfigureAwait(true);
        var state = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.Begin, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Capture)
            .ConfigureAwait(true);
        ProjectionBatchResult? checkpoint = null;
        if (state.OriginalCheckpointIntent is { } original)
        {
            var recovered = await TextMaintenanceReplay.ApplyAndCommitAsync(children, request, original, writer)
                .ConfigureAwait(true);
            state = recovered.State;
            checkpoint = recovered.Checkpoint;
        }
        var maximumPages = Math.Min(options.MaximumReplayPages,
            (routing.MaximumTotalFrames - ReservedFrames) / FramesPerPage);
        if (maximumPages <= EmptyPages)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded); }
        var replayed = await TextMaintenanceReplay.RunAsync(children, request, state, checkpoint,
            maximumPages, writer).ConfigureAwait(true);
        state = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.Verify, token).ConfigureAwait(true);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Completed)
            .ConfigureAwait(true);
        return new(request.CommandId, request.Consumer, request.IndexGeneration, TextIndexMaintenancePhase.Completed,
            state.Source, state.TrackedRecords, state.IndexSha256, replayed, IndexedThroughSequence: state.ThroughSequence);
    }
}
