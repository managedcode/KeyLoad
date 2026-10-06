using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Validates the fixed producer shape and admits its native encoded bytes before yield.</summary>
internal sealed class NativeCqrsStreamAdmission(
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer, Guid requestId, IOptions<GrainRoutingOptions> options)
{
    private long aggregateBytes;
    private int count;
    private bool terminal;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        CancellationToken cancellationToken)
    {
        const int CountStep = 1;

        if (terminal)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        var nextCount = checked(count + CountStep);
        if (nextCount > GrainRequestStreamProtocol.MaximumChunks)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        ValidateShape(chunk, nextCount);
        var maximumBytes = chunk.Kind switch
        {
            CqrsStreamChunkKind.Started => options.Value.MaximumStartedBytes,
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
            || chunk.ProgressResult.Value.Value.RequestId != requestId || chunk.Final is not null
            || chunk.Message is not null || chunk.EventId is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private void ValidateFinalShape(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        int nextCount)
    {
        const int EmptyNextCount = 1;
        const int ValidateFinalShapePresentCount = 1;
        const int StartedAndTerminalChunkCount = 2;
        const int NextCountFirstCount = 1;
        const int NextCountValidationBound = 2;
        const int ValidateFinalShapeEmptyNextCount = 2;

        var earlyFailure = chunk.Kind == CqrsStreamChunkKind.Failed && nextCount == EmptyNextCount;
        var expectedSequence = earlyFailure ? ValidateFinalShapePresentCount : StartedAndTerminalChunkCount;
        if (nextCount is < NextCountFirstCount or > NextCountValidationBound || (!earlyFailure && nextCount != ValidateFinalShapeEmptyNextCount)
            || chunk.Sequence != expectedSequence || chunk.ProgressResult is not null
            || chunk.Final is not { } final || chunk.Message is not null || chunk.EventId is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        ValidateTerminal(chunk.Kind, final);
    }

    private void ValidateTerminal(CqrsStreamChunkKind kind, Result<GrainOperationReply> final)
    {
        if (final.IsSuccess)
        {
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
