using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static partial class StorageCopyDiagnostics
{
    private const int CopyExcludedEvent = 2301;
    private const int CopyGapCompletedEvent = 2302;
    private const string CopyExcludedMessage = "Storage copy on silo {Silo} is excluded from owner selection with safe error {ErrorCode}.";
    private const string CopyGapCompletedMessage = "Canonical sequence {Sequence} was completed on {Copies} lagging storage copies before the next write.";

    [LoggerMessage(EventId = CopyExcludedEvent, Level = LogLevel.Warning, Message = CopyExcludedMessage)]
    internal static partial void CopyExcluded(ILogger logger, SiloAddress silo, ErrorCode errorCode);

    [LoggerMessage(EventId = CopyGapCompletedEvent, Level = LogLevel.Warning, Message = CopyGapCompletedMessage)]
    internal static partial void CopyGapCompleted(ILogger logger, long sequence, int copies);
}
