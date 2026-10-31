using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Validates the fixed producer shape and admits its execution encoded bytes before yield.</summary>
internal sealed class CqrsStreamAdmission(
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer, Guid requestId, IOptions<GrainRoutingOptions> options, CqrsStreamPurpose? purpose = null)
{
    private const int EmptyCount = 0;
    private long aggregateBytes;
    private int count;
    private bool terminal;
    private readonly TextMaintenanceStreamShape text = new(requestId);
    private AnnMaintenanceStreamShape? ann;
    private OnlineTextMaintenanceStreamShape? online;

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
        if (nextCount > (purpose?.HasLongProfile == true ? options.Value.MaximumStreamFrames : GrainRequestStreamProtocol.MaximumChunks))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        var maximumBytes = chunk.Kind switch
        {
            CqrsStreamChunkKind.Started or CqrsStreamChunkKind.Progress => options.Value.MaximumStartedBytes,
            CqrsStreamChunkKind.Completed => options.Value.MaximumCompletedBytes,
            CqrsStreamChunkKind.Failed => options.Value.MaximumFailedBytes,
            _ => throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest)
        };
        var chunkBytes = GrainByteCounter.Measure(serializer: serializer, value: chunk, maximumBytes: maximumBytes, cancellationToken: cancellationToken, options: options);
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

    internal (bool HasValue, Exception? Value) CompletionFailure(CancellationToken cancellationToken)
    {
        if (terminal)
        {
            return (false, default(Exception?));
        }

        return cancellationToken.IsCancellationRequested
            ? (true, (Exception?)(new OperationCanceledException(cancellationToken)))
            : (true, ((Exception?)(Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest))));
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
            CqrsProgressPayloadShape.RequireSingle(progress.Value);
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
        if (chunk.Kind == CqrsStreamChunkKind.Progress && purpose?.IsOnlineTextMaintenance == true && count > EmptyCount)
        {
            (online ??= new OnlineTextMaintenanceStreamShape(requestId)).Admit(chunk, nextCount);
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
            || chunk.ProgressResult.Value.Value is { AnnPhase: not null } or { TextPhase: not null } or { OnlineTextPhase: not null }
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
            || (chunk.Kind == CqrsStreamChunkKind.Completed && purpose?.IsOnlineTextMaintenance == true && online?.Completed == false))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        ValidateTerminal(chunk.Kind, final);
    }

    private void ValidateTerminal(CqrsStreamChunkKind kind, Result<GrainOperationReply> final)
    {
        if (final.IsSuccess)
        {
            var reply = final.Value;
            if (kind != CqrsStreamChunkKind.Completed || final.Problem is not null
                || reply.Result is null || GrainReplyFactory.Measure(reply) > options.Value.MaximumReplyBytes
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
