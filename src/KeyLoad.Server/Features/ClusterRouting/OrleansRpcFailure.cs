using global::Orleans.Runtime;

namespace KeyLoad.Server;

/// <summary>Closed native failure categories for an initial request-grain RPC.</summary>
internal enum OrleansRpcFailureCategory
{
    /// <summary>The native Orleans runtime could not complete the call.</summary>
    Orleans,
    /// <summary>The initial call exceeded its native response deadline.</summary>
    Timeout
}

/// <summary>Classifies initial native RPC failures without claiming storage damage or retrying a command.</summary>
internal static class OrleansRpcFailure
{
    private const string ReadDetail = "The Orleans request could not complete.";
    private const string CommandDetail = "The write outcome is unknown. Retry the same command ID.";
    private const string InvalidFailure = "Only a native Orleans RPC failure can be classified.";
    private const string FailureMessage = "Initial Orleans RPC failed: {RequestId} ({Category}) with {ErrorCode}.";
    private const int FailureEventId = 1003;
    private static readonly Action<ILogger, Guid, OrleansRpcFailureCategory, ErrorCode, Exception?> LogFailure =
        LoggerMessage.Define<Guid, OrleansRpcFailureCategory, ErrorCode>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);

    internal static bool IsNative(Exception error) => error is OrleansException or TimeoutException;

    internal static KeyLoadException Translate(Exception error, bool command, Guid requestId, ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(logger);
        var category = error switch
        {
            OrleansException => OrleansRpcFailureCategory.Orleans,
            TimeoutException => OrleansRpcFailureCategory.Timeout,
            _ => throw new ArgumentException(InvalidFailure, nameof(error))
        };
        var code = command ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
        LogFailure(logger, requestId, category, code, null);
        return Errors.Fail(code, command ? CommandDetail : ReadDetail);
    }
}
