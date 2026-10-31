using System.Text.Json;
using ManagedCode.Communication;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class GrainReplyFactory
{
    private const string Cancelled = "The database request was cancelled.";
    private const string Unavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string InterruptedWrite = "The write outcome is unknown. Retry the same command ID.";

    internal static GrainOperationReply Value(object? value) => new()
    {
        Result = new GrainValue(value)
    };

    internal static GrainValue RequireValue(GrainOperationReply reply)
        => reply.Error is null && reply.Result is { } result ? result
            : throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);

    internal static GrainOperationReply Operation(Result<object> result, CancellationToken cancellationToken)
    {
        if (result.IsFailed)
        {
            return Rejected(code: Errors.Code(result.Problem).Value!.Value, detail: result.Problem.Detail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Value(result.Value);
    }

    internal static Result<GrainOperationReply> StreamResult(GrainOperationReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Error is { } code)
        {
            if (reply.Result is not null || reply.SafeDetail is not { } detail)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }

            return Result<GrainOperationReply>.Fail(GrainRequestStreamProblem.Create(code, detail));
        }

        if (reply.Result is null || reply.SafeDetail is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        return Result<GrainOperationReply>.Succeed(reply);
    }

    internal static GrainOperationReply Failure(Exception error, bool command, ILogger? diagnostics, Guid requestId = default, GrainFailureStage stage = GrainFailureStage.EnvelopeVerification, CancellationToken cancellationToken = default)
    {
        var encodingFailure = command
            && error.Data[GrainFailureDiagnostics.StageMetadataKey] is GrainFailureStage.ReplyEncoding;
        var code = encodingFailure ? ErrorCode.UnknownWriteOutcome : FailureCode(error, command, cancellationToken);
        GrainFailureDiagnostics.Log(diagnostics, error, requestId, stage, code);
        if (encodingFailure)
        {
            return Rejected(code: code, detail: InterruptedWrite);
        }
        if (error is KeyLoadException failure)
        {
            return Rejected(code: code, detail: failure.Message);
        }

        if (error is OperationCanceledException)
        {
            return Rejected(code: code, detail: CancellationDetail(code));
        }

        if (error is JsonException or ArgumentException)
        {
            return Rejected(code: code, detail: GrainRoutingProtocol.InvalidRequest);
        }

        return Rejected(code: code, detail: Unavailable);
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
        SafeDetail = detail is { Length: > 0 } ? detail : Unavailable
    };
}
