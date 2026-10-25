using KeyLoad.Core.Messaging;

namespace KeyLoad.Core;
/// <summary>Owns the SagaDefinitionValidation operation contract.</summary>
internal static class SagaDefinitionValidation
{
    internal static CompareExchangeSaga NormalizeSagaRequest(CompareExchangeSaga request, PartitionRef partition, DateTimeOffset now)
    {
        if (request.SagaId == Guid.Empty || request.ExpectedRevision < SagaCommands.SagaCommandsInitialSequence || !Enum.IsDefined(request.Phase))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        var stateJson = JsonData.Validate(request.StateJson);
        if (request.Phase == SagaPhase.TimedOut)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        if (request.Phase is SagaPhase.Completed or SagaPhase.Cancelled)
        {
            if (request.Deadline is not null || request.Timeout is not null)
            {
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
            }

            return request with
            {
                StateJson = stateJson
            };
        }

        if ((request.Deadline is null) != (request.Timeout is null))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        var deadline = request.Deadline;
        if (deadline is { } deadlineValue && (deadlineValue.Offset != TimeSpan.Zero || deadlineValue <= now))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        if (request.Timeout is not { } timeout)
        {
            return request with
            {
                StateJson = stateJson
            };
        }

        SagaDefinitionValidation.ValidateTimeoutDefinition(timeout, request.Lane, partition, deadline!.Value);
        return request with
        {
            StateJson = stateJson,
            Timeout = SagaDefinitionValidation.NormalizeTimeoutDefinition(timeout)
        };
    }

    internal static SagaTimeoutDefinition NormalizeTimeoutDefinition(SagaTimeoutDefinition timeout)
    {
        JsonData.Identifier(timeout.Queue.Queue);
        var payload = JsonData.Validate(timeout.PayloadJson);
        var headers = JsonData.Validate(timeout.HeadersJson);
        if (timeout.OrderingKey is not null)
        {
            JsonData.Identifier(timeout.OrderingKey);
        }

        return timeout with
        {
            PayloadJson = payload,
            HeadersJson = headers
        };
    }

    internal static void ValidateTimeoutDefinition(SagaTimeoutDefinition timeout, QueueLaneRef source, PartitionRef partition, DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(timeout);
        if (timeout.Queue is null || timeout.Queue.Partition != partition || timeout.Queue.Partition != source.Partition || timeout.TimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl.Ticks > SagaCommands.MaximumTimeoutTtlTicks))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        _ = SagaCommands.AddTtl(deadline, timeout.TimeToLive);
    }
}
