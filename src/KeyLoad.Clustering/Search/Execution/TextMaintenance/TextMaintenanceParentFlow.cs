using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class TextMaintenanceParentFlow
{
    private const long InitialSequence = 0;
    private static readonly string[] MutationKinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument];

    internal static async Task<TextIndexMaintenanceResult> RunAsync(MaintenanceChildCalls children,
        TextIndexMaintenanceRequest request, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        if (request.Mode == TextIndexMaintenanceMode.Release)
        {
            return await TextMaintenanceRelease.RunAsync(children, request, token);
        }
        var configureId = TextMaintenanceChildIdentity.Create(request.CommandId,
            TextIndexMaintenancePhase.Configure, InitialSequence);
        var configured = await children.CommandAsync<ConfigureProjectionConsumerRequest, ProjectionConsumerInfo>(
            OperationKind.ConfigureProjectionConsumer,
            new(configureId, request.Consumer, new(request.IndexGeneration, [request.Collection], [.. MutationKinds])),
            configureId, token);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Configure);
        var state = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.Begin, token);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Capture);
        TextMaintenanceReplay.RequireOriginalParent(request, configured, state);
        ProjectionBatchResult? checkpoint = null;
        if (state.OriginalCheckpointIntent is { } original)
        {
            var recovered = await TextMaintenanceReplay.ApplyAndCommitAsync(children, request, original, writer);
            state = recovered.State;
            checkpoint = recovered.Checkpoint;
        }
        var replayed = await TextMaintenanceReplay.RunAsync(children, request, state, checkpoint, writer);
        state = await children.CapabilityAsync(request, TextMaintenanceCapabilityKind.Verify, token);
        await TextMaintenanceProgress.WriteAsync(writer, children.ParentRequestId, TextIndexMaintenancePhase.Completed);
        return new(request.CommandId, request.Consumer, request.IndexGeneration, TextIndexMaintenancePhase.Completed,
            state.Source, state.TrackedRecords, state.IndexSha256, replayed, IndexedThroughSequence: state.ThroughSequence);
    }
}
