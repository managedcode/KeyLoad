using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static class GrainReplyFactory
{
    private const int MaximumDetailCharacters = 4_096;
    private const string Cancelled = "The database request was cancelled.";
    private const string Unavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string InterruptedWrite = "The write outcome is unknown. Retry the same command ID.";

    internal static GrainOperationReply Value(object? value, CancellationToken cancellationToken)
    {
        using var stream = new GrainBoundedPayloadStream(GrainRoutingProtocol.MaximumReplyBytes, cancellationToken);
        NativeSerialization.Serialize(new GrainValue(value), stream);
        return new() { Payload = stream.Complete() };
    }

    internal static GrainOperationReply Operation(OperationResult result, CancellationToken cancellationToken)
    {
        if (result.Error is { } error)
        {
            return Rejected(error, result.SafeDetail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Value(result.NativeValue, cancellationToken);
    }

    internal static GrainOperationReply Failure(Exception error, bool command, ILogger? diagnostics,
        Guid requestId = default, GrainFailureStage stage = GrainFailureStage.EnvelopeVerification,
        CancellationToken cancellationToken = default)
    {
        var code = FailureCode(error, command, cancellationToken);
        GrainFailureDiagnostics.Log(diagnostics, error, requestId, stage, code);
        if (error is KeyLoadException failure)
        {
            return Rejected(code, failure.Message);
        }

        if (error is OperationCanceledException)
        {
            return Rejected(code, CancellationDetail(code));
        }

        if (error is JsonException or ArgumentException)
        {
            return Rejected(code, GrainRoutingProtocol.InvalidRequest);
        }

        return Rejected(code, Unavailable);
    }

    private static ErrorCode FailureCode(Exception error, bool command, CancellationToken cancellationToken) => error switch
    {
        KeyLoadException failure => failure.Code,
        OperationCanceledException when command => ErrorCode.UnknownWriteOutcome,
        OperationCanceledException => cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.OwnershipLost,
        JsonException or ArgumentException => ErrorCode.Validation,
        _ => command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost
    };

    private static string CancellationDetail(ErrorCode code) => code switch
    {
        ErrorCode.Cancelled => Cancelled,
        ErrorCode.UnknownWriteOutcome => InterruptedWrite,
        _ => Unavailable
    };

    private static GrainOperationReply Rejected(ErrorCode code, string? detail) => new()
    {
        Error = Enum.IsDefined(code) ? code : ErrorCode.OwnershipLost,
        SafeDetail = detail is { Length: > 0 and <= MaximumDetailCharacters } ? detail : Unavailable
    };
}
