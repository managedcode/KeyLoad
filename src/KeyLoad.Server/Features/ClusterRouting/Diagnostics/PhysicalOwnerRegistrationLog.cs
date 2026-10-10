namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationLog
{
    internal const string CompletedMessage = "Physical owner registration completed.";
    private const int CompletedEvent = 1005;
    private static readonly Action<ILogger, Exception?> Completed = LoggerMessage.Define(LogLevel.Information,
        new EventId(CompletedEvent), CompletedMessage);
    internal static void Succeeded(ILogger logger) => Completed(logger, null);
    private const int FailureEvent = 1004;
    private const string FailureMessage = "Physical owner registration stopped with {ErrorCode} at {Stage} ({Category}); DeadlineCancelled={DeadlineCancelled}; StoppingCancelled={StoppingCancelled}.";
    private static readonly Action<ILogger, ErrorCode, PhysicalOwnerRegistrationStage,
        PhysicalOwnerRegistrationFailureCategory, bool, bool, Exception?> Failure =
        LoggerMessage.Define<ErrorCode, PhysicalOwnerRegistrationStage, PhysicalOwnerRegistrationFailureCategory, bool, bool>(
            LogLevel.Warning, new EventId(FailureEvent), FailureMessage);

    internal static void Failed(ILogger logger, Exception error, PhysicalOwnerRegistrationStage stage,
        bool deadlineCancelled, bool stoppingCancelled)
        => Failure(logger, error is KeyLoadException known ? known.Code : ErrorCode.UnknownWriteOutcome,
            stage, Category(error), deadlineCancelled, stoppingCancelled, null);

    private static PhysicalOwnerRegistrationFailureCategory Category(Exception error) => error switch
    {
        KeyLoadException => PhysicalOwnerRegistrationFailureCategory.Domain,
        OperationCanceledException => PhysicalOwnerRegistrationFailureCategory.Cancellation,
        System.Text.Json.JsonException or FormatException or ArgumentException => PhysicalOwnerRegistrationFailureCategory.Protocol,
        _ => PhysicalOwnerRegistrationFailureCategory.Unexpected
    };
}
