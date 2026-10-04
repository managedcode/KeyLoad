using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Validates the fixed producer shape and admits its native encoded bytes before yield.</summary>
internal sealed class NativeCqrsStreamAdmission(
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer, Guid requestId)
{
    private long aggregateBytes;
    private int count;
    private bool terminal;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk,
        CancellationToken cancellationToken)
    {
        if (terminal)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        var nextCount = checked(count + 1);
        if (nextCount > GrainRequestStreamProtocol.MaximumChunks)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        ValidateShape(chunk, nextCount);
        var maximumBytes = chunk.Kind switch
        {
            CqrsStreamChunkKind.Started => GrainRequestStreamProtocol.MaximumStartedBytes,
            CqrsStreamChunkKind.Completed => GrainRequestStreamProtocol.MaximumCompletedBytes,
            CqrsStreamChunkKind.Failed => GrainRequestStreamProtocol.MaximumFailedBytes,
            _ => throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest)
        };
        var chunkBytes = GrainNativeByteCounter.Measure(serializer, chunk, maximumBytes, cancellationToken);
        var nextAggregate = checked(aggregateBytes + chunkBytes);
        if (nextAggregate > GrainRequestStreamProtocol.MaximumAggregateBytes)
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
            if (nextCount != 1 || chunk.Sequence != 1 || chunk.ProgressResult is not { IsSuccess: true, Value: not null }
                || chunk.ProgressResult.Value.Problem is not null
                || chunk.ProgressResult.Value.Value.RequestId != requestId || chunk.Final is not null
                || chunk.Message is not null || chunk.EventId is not null
                || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }

            return;
        }

        var earlyFailure = chunk.Kind == CqrsStreamChunkKind.Failed && nextCount == 1;
        var expectedSequence = earlyFailure ? 1 : 2;
        if (nextCount is < 1 or > 2 || (!earlyFailure && nextCount != 2)
            || chunk.Sequence != expectedSequence || chunk.ProgressResult is not null
            || chunk.Final is not { } final || chunk.Message is not null || chunk.EventId is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        ValidateTerminal(chunk.Kind, final);
    }

    private static void ValidateTerminal(CqrsStreamChunkKind kind, Result<GrainOperationReply> final)
    {
        if (final.IsSuccess)
        {
            if (kind != CqrsStreamChunkKind.Completed || final.Problem is not null
                || final.Value is not { } reply || reply.Payload.IsEmpty || reply.Payload.Length > GrainRoutingProtocol.MaximumReplyBytes
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

        GrainRequestStreamProblem.Validate(problem);
    }
}
