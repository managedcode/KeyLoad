namespace KeyLoad.Server;

/// <summary>Owns the stable event ID and strongly typed, bounded failure log template.</summary>
internal static class RequestFailureDiagnosticLog
{
    private const string FailureLog = "Database request failed at {FailurePhase} with {FailureCategory} for operation {OperationId} at {FailureUtc}.";
    private const int FailureEventId = 1001;

    private static readonly Action<ILogger, RequestFailurePhase, RequestFailureCategory, Guid?, string, Exception?> FailureDelegate =
        LoggerMessage.Define<RequestFailurePhase, RequestFailureCategory, Guid?, string>(
            LogLevel.Error, new EventId(FailureEventId), FailureLog);

    internal static void Failure(ILogger logger, RequestFailurePhase phase, RequestFailureCategory category,
        Guid? operationId, string failureUtc)
        => FailureDelegate(logger, phase, category, operationId, failureUtc, null);
}
