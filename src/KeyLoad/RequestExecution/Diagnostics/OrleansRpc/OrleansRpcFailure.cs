using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Classifies stream interruption without claiming storage damage or retrying a command.</summary>
internal static class OrleansRpcFailure
{
    private const string ReadDetail = "The Orleans request could not complete.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string InvalidFailure = "Fatal runtime failures cannot be converted to request outcomes.";
    private const string FailureMessage = "Orleans request stream failed: {RequestId} ({Category}) with {ErrorCode}; Stage={Stage}; Kind={Kind}.";
    private const int FailureEventId = 1003;
    private const string UnobservedOriginalCause = "Unobserved";
    private const string OriginalCauseMessage = "Orleans original stream cause: {RequestId}; CommandId={CommandId}; ReadKind={ReadKind}; CommandKind={CommandKind}; OriginalErrorCode={OriginalErrorCode}; Kind={Kind}.";
    private static readonly Action<ILogger, Guid, Guid, string, string, string, OrleansRpcFailureKind, Exception?> LogOriginalCause =
        LoggerMessage.Define<Guid, Guid, string, string, string, OrleansRpcFailureKind>(LogLevel.Warning,
            new EventId(FailureEventId), OriginalCauseMessage);
    private static readonly Action<ILogger, Guid, OrleansRpcFailureCategory, ErrorCode, OrleansRpcFailureStage, OrleansRpcFailureKind, Exception?> LogFailure =
        LoggerMessage.Define<Guid, OrleansRpcFailureCategory, ErrorCode, OrleansRpcFailureStage, OrleansRpcFailureKind>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);

    internal static KeyLoadException Translate(Exception error, bool command, Guid requestId, ILogger logger,
        CancellationToken cancellationToken)
        => Translate(error, command, requestId, logger, OrleansRpcFailureStage.Unobserved, cancellationToken);

    internal static KeyLoadException Translate(Exception error, bool command, Guid requestId, ILogger logger,
        OrleansRpcFailureStage stage, CancellationToken cancellationToken)
        => Translate(error, command, requestId, logger, stage, null, cancellationToken);

    internal static KeyLoadException Translate(Exception error, bool command, Guid requestId, ILogger logger,
        OrleansRpcFailureStage stage, OrleansRpcFailureOperation? originalOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(logger);
        if (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            throw new ArgumentException(InvalidFailure, nameof(error));
        }
        if (!command && cancellationToken.IsCancellationRequested)
        {
            return Errors.Fail(ErrorCode.Cancelled, ReadDetail);
        }
        var category = error switch
        {
            OrleansException => OrleansRpcFailureCategory.Orleans,
            TimeoutException => OrleansRpcFailureCategory.Timeout,
            OperationCanceledException => OrleansRpcFailureCategory.Cancellation,
            KeyLoadException or System.Text.Json.JsonException or FormatException => OrleansRpcFailureCategory.Protocol,
            _ => OrleansRpcFailureCategory.Unexpected
        };
        var code = command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
        LogFailure(logger, requestId, category, code, stage, Kind(error), null);
        OriginalCause(logger, error, requestId, originalOperation);
        return Errors.Fail(code, command ? CommandDetail : ReadDetail);
    }

    private static void OriginalCause(ILogger logger, Exception error, Guid requestId,
        OrleansRpcFailureOperation? operation)
    {
        ErrorCode? originalCode = error is KeyLoadException typed ? typed.Code : null;
        LogOriginalCause(logger, requestId, operation?.CommandId ?? Guid.Empty,
            ClosedName(operation?.ReadKind), ClosedName(operation?.CommandKind), ClosedName(originalCode),
            Kind(error), null);
    }

    private static string ClosedName<T>(T? value) where T : struct, Enum
        => value is { } observed && Enum.IsDefined(observed) ? observed.ToString() : UnobservedOriginalCause;

    private static OrleansRpcFailureKind Kind(Exception error) => error switch
    {
        OrleansException => OrleansRpcFailureKind.Orleans,
        TimeoutException => OrleansRpcFailureKind.Timeout,
        OperationCanceledException => OrleansRpcFailureKind.Cancellation,
        KeyLoadException => OrleansRpcFailureKind.KeyLoad,
        System.Text.Json.JsonException => OrleansRpcFailureKind.Json,
        FormatException => OrleansRpcFailureKind.Format,
        ObjectDisposedException => OrleansRpcFailureKind.Disposed,
        InvalidOperationException => OrleansRpcFailureKind.InvalidOperation,
        NotSupportedException => OrleansRpcFailureKind.NotSupported,
        ArgumentException => OrleansRpcFailureKind.Argument,
        System.Runtime.Serialization.SerializationException => OrleansRpcFailureKind.Serialization,
        AggregateException => OrleansRpcFailureKind.Aggregate,
        IOException => OrleansRpcFailureKind.InputOutput,
        _ => OrleansRpcFailureKind.Other
    };
}
