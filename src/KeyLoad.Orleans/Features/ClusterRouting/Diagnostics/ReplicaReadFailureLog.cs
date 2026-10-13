using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static class ReplicaReadFailureLog
{
    private const string StageKey = "KeyLoad.Replication.ReadBarrier.Stage";
    private const string FlagsKey = "KeyLoad.Replication.ReadBarrier.Flags";
    private const int FirstStage = 1;
    private const int LastStage = 8;
    private const int NoFlags = 0;
    private const int MaximumFlags = 7;
    private const int FailureEventId = 4;
    internal const string Prefix = "Replica read barrier rejected:";
    private const string FailureTemplate = "Replica read barrier rejected: {RequestId}; Stage={Stage}; Flags={Flags}.";
    private static readonly Action<ILogger, Guid, int, int, Exception?> Write =
        LoggerMessage.Define<Guid, int, int>(LogLevel.Warning, new EventId(FailureEventId),
            FailureTemplate);

    internal static void Observe(ILogger? logger, Exception error, Guid requestId)
    {
        if (logger is not null && requestId != Guid.Empty
            && error.Data[StageKey] is int stage && stage >= FirstStage && stage <= LastStage
            && error.Data[FlagsKey] is int flags && flags >= NoFlags && flags <= MaximumFlags)
        { Write(logger, requestId, stage, flags, null); }
    }
}
