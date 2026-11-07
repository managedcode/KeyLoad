using KeyLoad.Server.Authorization.Contracts;
using OpenTelemetry.Logs;

namespace KeyLoad.Server;

/// <summary>Owns the stable event ID and strongly typed, bounded failure log template.</summary>
internal static class RequestFailureDiagnosticLog
{
    private const string FailureLog = "Database request failed at {FailurePhase} with {FailureCategory} for operation {OperationId} at {FailureUtc}.";
    private const int FailureEventId = 1001;
    private const string Category = "KeyLoad.Server.ServerErrorMiddleware";
    private const int MaximumAttributes = 6;
    private const string PhaseKey = "FailurePhase";
    private const string CategoryKey = "FailureCategory";
    private const string ExportPrefix = "Database request failed at ";
    private const string CategorySeparator = " with ";

    private static readonly Action<ILogger, RequestFailurePhase, RequestFailureCategory, Guid?, string, Exception?> FailureDelegate =
        LoggerMessage.Define<RequestFailurePhase, RequestFailureCategory, Guid?, string>(
            LogLevel.Error, new EventId(FailureEventId), FailureLog);

    internal static void Failure(ILogger logger, RequestFailurePhase phase, RequestFailureCategory category,
        Guid? operationId, string failureUtc)
        => FailureDelegate(logger, phase, category, operationId, failureUtc, null);

    /// <summary>Preserves only the owning event's validated closed enums before all native attributes are removed.</summary>
    internal static string ExportBody(LogRecord record)
    {
        if (record.CategoryName != Category || record.EventId.Id != FailureEventId
            || (record.Attributes /* KLD0041: Inspect only this owner's bounded native log attribute list to validate two closed enum values; no arbitrary field is exported. */) is not { Count: <= MaximumAttributes } attributes)
        {
            return HttpTelemetryPrivacyPolicy.RuntimeBody;
        }
        RequestFailurePhase? phase = null;
        RequestFailureCategory? category = null;
        foreach (var attribute in attributes)
        {
            if (attribute.Key == PhaseKey
                && ((attribute.Value /* KLD0041: Accept the native logger's boxed value only for closed RequestFailurePhase validation. */) is RequestFailurePhase knownPhase /* KLD0042: Recover only a defined closed dispatch phase from native logger state. */) && Enum.IsDefined(knownPhase))
            {
                phase = knownPhase;
            }
            if (attribute.Key == CategoryKey
                && ((attribute.Value /* KLD0041: Accept the native logger's boxed value only for closed RequestFailureCategory validation. */) is RequestFailureCategory knownCategory /* KLD0042: Recover only a defined closed failure category from native logger state. */) && Enum.IsDefined(knownCategory))
            {
                category = knownCategory;
            }
        }
        return phase is { } known && category is { } failure
            ? $"{ExportPrefix}{known}{CategorySeparator}{failure}"
            : HttpTelemetryPrivacyPolicy.RuntimeBody;
    }
}
