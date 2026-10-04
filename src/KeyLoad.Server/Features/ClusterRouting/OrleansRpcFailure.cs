using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Closed failure categories for a native request stream before a validated public response.</summary>
internal enum OrleansRpcFailureCategory
{
    /// <summary>The native Orleans runtime could not complete the call.</summary>
    Orleans,
    /// <summary>The initial call exceeded its native response deadline.</summary>
    Timeout,
    /// <summary>The native enumeration was cancelled.</summary>
    Cancellation,
    /// <summary>The stream did not satisfy the closed product protocol.</summary>
    Protocol,
    /// <summary>Another nonfatal failure interrupted enumeration or cleanup.</summary>
    Unexpected
}

/// <summary>Classifies stream interruption without claiming storage damage or retrying a command.</summary>
internal static class OrleansRpcFailure
{
    private const string ReadDetail = "The Orleans request could not complete.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string InvalidFailure = "Fatal runtime failures cannot be converted to request outcomes.";
    private const string FailureMessage = "Orleans request stream failed: {RequestId} ({Category}) with {ErrorCode}.";
    private const int FailureEventId = 1003;
    private static readonly Action<ILogger, Guid, OrleansRpcFailureCategory, ErrorCode, Exception?> LogFailure =
        LoggerMessage.Define<Guid, OrleansRpcFailureCategory, ErrorCode>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);

    internal static KeyLoadException Translate(Exception error, bool command, Guid requestId, ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(logger);
        if (!NativeCqrsBoundaryErrors.IsNonFatal(error))
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
        LogFailure(logger, requestId, category, code, null);
        return Errors.Fail(code, command ? CommandDetail : ReadDetail);
    }
}
