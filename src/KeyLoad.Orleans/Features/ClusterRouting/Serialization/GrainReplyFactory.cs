using Microsoft.Extensions.Options;
using System.Text.Json;
using ManagedCode.Communication;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static class GrainReplyFactory
{
    private const int RejectedDetailEmptyCount = 0;

    private const string Cancelled = "The database request was cancelled.";
    private const string Unavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string InterruptedWrite = "The write outcome is unknown. Retry the same command ID.";

    internal static GrainOperationReply Value(object? value, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
    {
        using var stream = new GrainBoundedPayloadStream(options.Value.MaximumReplyBytes, cancellationToken);
        NativeSerialization.Serialize(new GrainValue(value), stream);
        return new() { Payload = stream.Complete() };
    }

    internal static GrainOperationReply Operation(OperationResult result, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
    {
        if (result.Error is { } error)
        {
            return Rejected(code: error, detail: result.SafeDetail, options: options);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Value(value: result.NativeValue, cancellationToken: cancellationToken, options: options);
    }

    internal static Result<GrainOperationReply> StreamResult(GrainOperationReply reply, IOptions<GrainRoutingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Error is { } code)
        {
            if (!reply.Payload.IsEmpty || reply.SafeDetail is not { } detail)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }

            return Result<GrainOperationReply>.Fail(GrainRequestStreamProblem.Create(code: code, detail: detail, options: options));
        }

        if (reply.Payload.IsEmpty || reply.Payload.Length > options.Value.MaximumReplyBytes
            || reply.SafeDetail is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }

        return Result<GrainOperationReply>.Succeed(reply);
    }

    internal static GrainOperationReply Failure(Exception error, bool command, ILogger? diagnostics, IOptions<GrainRoutingOptions> options, Guid requestId = default, GrainFailureStage stage = GrainFailureStage.EnvelopeVerification, CancellationToken cancellationToken = default)
    {
        var code = FailureCode(error, command, cancellationToken);
        GrainFailureDiagnostics.Log(diagnostics, error, requestId, stage, code);
        if (error is KeyLoadException failure)
        {
            return Rejected(code: code, detail: failure.Message, options: options);
        }

        if (error is OperationCanceledException)
        {
            return Rejected(code: code, detail: CancellationDetail(code), options: options);
        }

        if (error is JsonException or ArgumentException)
        {
            return Rejected(code: code, detail: GrainRoutingProtocol.InvalidRequest, options: options);
        }

        return Rejected(code: code, detail: Unavailable, options: options);
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

    private static GrainOperationReply Rejected(ErrorCode code, string? detail, IOptions<GrainRoutingOptions> options) => new()
    {
        Error = Enum.IsDefined(code) ? code : ErrorCode.OwnershipLost,
        SafeDetail = detail is { Length: > RejectedDetailEmptyCount } && detail.Length <= options.Value.MaximumDetailCharacters ? detail : Unavailable
    };
}
