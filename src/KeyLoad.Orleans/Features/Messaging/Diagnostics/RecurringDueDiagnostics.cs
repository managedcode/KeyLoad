using KeyLoad.Core;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static partial class RecurringDueDiagnostics
{
    private const int PageRejectedEvent = 2201;
    private const int PrefixRejectedEvent = 2202;
    private const int DispatchFailedEvent = 2203;
    private const int ServiceFaultEvent = 2204;
    private const int DispatchDeadlineEvent = 2205;
    private const string PageRejectedMessage = "Due discovery rejected {RejectedCount} stored records.";
    private const string PrefixRejectedMessage = "Due discovery deferred a corrupt prefix with safe error {ErrorCode}.";
    private const string DispatchFailedMessage = "Due-work dispatch completed with safe error {ErrorCode}.";
    private const string ServiceFaultMessage = "Due-work coordinator cycle failed with safe error {ErrorCode}.";
    private const string DispatchDeadlineMessage = "Due-work dispatch exceeded its bounded deadline.";

    [LoggerMessage(EventId = PageRejectedEvent, Level = LogLevel.Warning, Message = PageRejectedMessage)]
    internal static partial void PageRejected(ILogger logger, int rejectedCount);

    [LoggerMessage(EventId = PrefixRejectedEvent, Level = LogLevel.Warning, Message = PrefixRejectedMessage)]
    internal static partial void PrefixRejected(ILogger logger, ErrorCode errorCode);

    [LoggerMessage(EventId = DispatchFailedEvent, Level = LogLevel.Warning, Message = DispatchFailedMessage)]
    internal static partial void DispatchFailed(ILogger logger, ErrorCode errorCode);

    [LoggerMessage(EventId = ServiceFaultEvent, Level = LogLevel.Warning, Message = ServiceFaultMessage)]
    internal static partial void ServiceFault(ILogger logger, ErrorCode errorCode);

    [LoggerMessage(EventId = DispatchDeadlineEvent, Level = LogLevel.Warning, Message = DispatchDeadlineMessage)]
    internal static partial void DispatchDeadline(ILogger logger);
}
