using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static class GrainReplyFactory
{
    private const int MaximumDetailCharacters = 4_096;
    private const string FailureLog = "Orleans database request failed.";
    private const string Cancelled = "The database request was cancelled.";
    private const string Unavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string InterruptedWrite = "The write outcome is unknown. Retry the same command ID.";
    private const string NullJson = "null";
    private const int FailureEventId = 2;
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(LogLevel.Error,
        new EventId(FailureEventId), FailureLog);

    internal static GrainOperationReply Value(object? value, CancellationToken cancellationToken)
    {
        using var stream = new GrainBoundedJsonStream(GrainRoutingProtocol.MaximumReplyBytes, cancellationToken);
        JsonSerializer.Serialize(stream, value, JsonDefaults.Options);
        return new() { Payload = stream.Complete() };
    }

    internal static GrainOperationReply Operation(OperationResult result, CancellationToken cancellationToken)
    {
        if (result.Error is { } error)
        {
            return Rejected(error, result.SafeDetail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var json = result.Json ?? NullJson;
        if (GrainPayloadJson.Utf8.GetByteCount(json) > GrainRoutingProtocol.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.ReplyBudgetExceeded);
        }

        return new() { Payload = GrainPayloadJson.Utf8.GetBytes(json) };
    }

    internal static GrainOperationReply Failure(Exception error, bool command, ILogger? diagnostics)
    {
        if (error is KeyLoadException failure)
        {
            return Rejected(failure.Code, failure.Message);
        }

        if (error is OperationCanceledException)
        {
            return Rejected(command ? ErrorCode.UnknownWriteOutcome : ErrorCode.Cancelled, command ? InterruptedWrite : Cancelled);
        }

        if (error is JsonException or ArgumentException)
        {
            return Rejected(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }

        if (diagnostics is not null)
        {
            LogFailure(diagnostics, null);
        }
        return Rejected(command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, Unavailable);
    }

    private static GrainOperationReply Rejected(ErrorCode code, string? detail) => new()
    {
        Error = Enum.IsDefined(code) ? code : ErrorCode.OwnershipLost,
        SafeDetail = detail is { Length: > 0 and <= MaximumDetailCharacters } ? detail : Unavailable
    };
}
