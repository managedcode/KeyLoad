using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Admits current typed public movement only through the original verified request and persisted administrator.</summary>
internal static class PartitionMoveParentExecution
{
    private const long InitialRevision = 0;
    private const string PhaseCompleted = "The controlled partition movement phase completed.";

    internal static async Task<PartitionMoveResult> ExecuteAsync(DecodedGrainRequest original,
        IServiceProvider services, TimeProvider clock, GrainRequestCodec codec, IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        token.ThrowIfCancellationRequested();
        var request = GrainNativePayload.ReadCommand<PartitionMoveRequest>(original.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        if (request.MoveId == Guid.Empty || original.Envelope.CommandId != request.MoveId
            || request.DestinationPhysicalShardId == Guid.Empty || request.ExpectedPlacementRevision < InitialRevision
            || request.Mode == PartitionMoveMode.None || !Enum.IsDefined(request.Mode))
        { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
        DatabaseEngine.ValidatePartition(request.Partition);
        var parent = services.GetService<IPartitionMovementParent>()
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, token);
        var phaseObservation = codec.HasPhaseObserver ? CreatePhaseObservation(codec, original, context) : null;
        return await parent.ExecuteAsync(original.Envelope, request, work,
            phase => writer.ProgressAsync(new GrainRequestProgress(original.Envelope.RequestId)
            { MovePhase = phase }, PhaseCompleted), phaseObservation, token).ConfigureAwait(true);
    }
    private static Func<GrainRequestPhase, CancellationToken, ValueTask> CreatePhaseObservation(GrainRequestCodec codec,
        DecodedGrainRequest original, IGrainContext context)
        => (phase, token) => phase is GrainRequestPhase.ParentStagePreflight or GrainRequestPhase.ParentTransferCloseFailed
            or GrainRequestPhase.ParentReceiverIssueAcknowledged or GrainRequestPhase.ParentReceiverIssueObserved
                or GrainRequestPhase.ParentFinalInstallPreflight
            ? codec.ObservePhaseAsync(original, phase, context, token)
            : throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
}
