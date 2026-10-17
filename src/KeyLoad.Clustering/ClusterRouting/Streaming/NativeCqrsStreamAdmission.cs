using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Validates the fixed producer shape and admits its native encoded bytes before yield.</summary>
internal sealed class NativeCqrsStreamAdmission(
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer, Guid requestId, IOptions<GrainRoutingOptions> options, NativeCqrsStreamPurpose? purpose = null)
{
    private const int EmptyCount = 0;
    private long aggregateBytes;
    private int count;
    private bool terminal;
    private EventFeedTailStreamShape? tail;
    private readonly TextMaintenanceStreamShape text = new(requestId);
    private AnnMaintenanceStreamShape? ann;
    private PartitionMovementStreamShape? movement;
    private OnlineTextMaintenanceStreamShape? online;
    private PlacementIntentStreamShape? placement;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        CancellationToken cancellationToken)
    {
        const int CountStep = 1;

        if (terminal)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        if (count > EmptyCount && chunk.Kind == CqrsStreamChunkKind.Progress)
        {
            purpose?.ResolveProgress();
        }
        var nextCount = checked(count + CountStep);
        if (nextCount > (purpose?.HasLongProfile == true ? options.Value.MaximumTotalFrames : GrainRequestStreamProtocol.MaximumChunks))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        var maximumBytes = chunk.Kind switch
        {
            CqrsStreamChunkKind.Progress when purpose?.IsEventFeedTail == true => options.Value.MaximumCompletedBytes,
            CqrsStreamChunkKind.Started or CqrsStreamChunkKind.Progress => options.Value.MaximumStartedBytes,
            CqrsStreamChunkKind.Completed => options.Value.MaximumCompletedBytes,
            CqrsStreamChunkKind.Failed => options.Value.MaximumFailedBytes,
            _ => throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest)
        };
        var chunkBytes = GrainNativeByteCounter.Measure(serializer: serializer, value: chunk, maximumBytes: maximumBytes, cancellationToken: cancellationToken, options: options);
        var nextAggregate = checked(aggregateBytes + chunkBytes);
        if (nextAggregate > options.Value.MaximumAggregateBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        ValidateShape(chunk, nextCount);
        count = nextCount;
        aggregateBytes = nextAggregate;
        terminal = chunk.IsTerminal;
    }

    internal Exception? CompletionFailure(CancellationToken cancellationToken)
    {
        if (terminal)
        {
            return null;
        }

        return cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(cancellationToken)
            : Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    }

    private void ValidateShape(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int nextCount)
    {
        if (chunk.Kind == CqrsStreamChunkKind.Started)
        {
            ValidateStartedShape(chunk, nextCount);
            purpose?.Started();
            return;
        }

        if (chunk.Kind == CqrsStreamChunkKind.Progress && chunk.ProgressResult is { IsSuccess: true, Value: not null } progress)
        {
            NativeCqrsProgressPayloadShape.RequireSingle(progress.Value);
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.IsTextMaintenance == true && count > EmptyCount)
        {
            text.Admit(chunk, nextCount);
            return;
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.IsAnnMaintenance == true && count > EmptyCount)
        {
            (ann ??= new AnnMaintenanceStreamShape(requestId)).Admit(chunk, nextCount);
            return;
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.TailRequest is { } request && count > EmptyCount)
        {
            (tail ??= new EventFeedTailStreamShape(requestId, request)).Admit(chunk, nextCount);
            return;
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.IsPartitionMovement == true && count > EmptyCount)
        {
            (movement ??= new PartitionMovementStreamShape(requestId)).Admit(chunk, nextCount);
            return;
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.IsOnlineTextMaintenance == true && count > EmptyCount)
        {
            (online ??= new OnlineTextMaintenanceStreamShape(requestId)).Admit(chunk, nextCount);
            return;
        }
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.PlacementRequest is { } placementRequest && count > EmptyCount)
        {
            (placement ??= new PlacementIntentStreamShape(requestId, placementRequest.IntentId, placementRequest.ExpectedRevision)).Admit(chunk, nextCount);
            return;
        }
        ValidateFinalShape(chunk, nextCount);
    }

    private void ValidateStartedShape(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        int nextCount)
    {
        const int EmptyNextCount = 1;
        const int EmptySequence = 1;

        if (nextCount != EmptyNextCount || chunk.Sequence != EmptySequence || chunk.ProgressResult is not { IsSuccess: true, Value: not null }
            || chunk.ProgressResult.Value.Problem is not null
            || chunk.ProgressResult.Value.Value.RequestId != requestId
            || chunk.ProgressResult.Value.Value is { AnnPhase: not null } or { TextPhase: not null } or { MovePhase: not null } or { OnlineTextPhase: not null } or { EventFeedTail: not null } or { PlacementIntent: not null }
            || chunk.Final is not null
            || chunk.Message is not null || chunk.EventId is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private void ValidateFinalShape(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        int nextCount)
    {
        const int FirstCount = 1;
        const int TerminalCount = 2;
        var earlyFailure = chunk.Kind == CqrsStreamChunkKind.Failed && nextCount == FirstCount;
        var longProfile = purpose?.HasLongProfile == true;
        if ((!earlyFailure && (count == EmptyCount || (!longProfile && nextCount != TerminalCount)))
            || chunk.Sequence != nextCount || chunk.ProgressResult is not null
            || chunk.Final is not { } final || chunk.Message is not null || chunk.EventId is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind)
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.IsTextMaintenance == true && !text.Completed)
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.IsAnnMaintenance == true && ann?.Completed != true)
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.IsEventFeedTail == true && tail?.HasDelivery != true)
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.IsOnlineTextMaintenance == true && online?.Completed == false)
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.PlacementRequest is not null && placement?.Completed == false))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        ValidateTerminal(chunk.Kind, final);
    }

    private void ValidateTerminal(CqrsStreamChunkKind kind, Result<GrainOperationReply> final)
    {
        if (final.IsSuccess)
        {
            if (final.Value is { Metadata: not null } counted)
            {
                if (kind != CqrsStreamChunkKind.Completed || final.Problem is not null
                    || purpose?.MetadataAdmission is not { } admission)
                {
                    throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
                }
                NativeCommandMetadataReplies.Require(counted, admission, options);
                return;
            }
            if (kind != CqrsStreamChunkKind.Completed || final.Problem is not null
                || final.Value is not { } reply || reply.Payload.IsEmpty || reply.Payload.Length > options.Value.MaximumReplyBytes
                || reply.Error is not null || reply.SafeDetail is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }

            return;
        }

        if (kind != CqrsStreamChunkKind.Failed || final.Value is not null
            || !final.TryGetProblem(out var problem))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        GrainRequestStreamProblem.Validate(problem: problem, options: options);
    }
}
