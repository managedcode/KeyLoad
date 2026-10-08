namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationLog
{
    internal const string CompletedMessage = "Physical owner registration completed.";
    private const int CompletedEvent = 1005;
    private static readonly Action<ILogger, Exception?> Completed = LoggerMessage.Define(LogLevel.Information,
        new EventId(CompletedEvent), CompletedMessage);
    internal static void Succeeded(ILogger logger) => Completed(logger, null);
    private const int FailureEvent = 1004;
    private const string FailureMessage = "Physical owner registration stopped with {ErrorCode}.";
    private static readonly Action<ILogger, ErrorCode, Exception?> Failure = LoggerMessage.Define<ErrorCode>(
        LogLevel.Warning, new EventId(FailureEvent), FailureMessage);

    internal static void Failed(ILogger logger, Exception error)
        => Failure(logger, error is KeyLoadException known ? known.Code : ErrorCode.UnknownWriteOutcome, null);
}
