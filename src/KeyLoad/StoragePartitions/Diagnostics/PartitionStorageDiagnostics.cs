namespace KeyLoad.Server;

internal static partial class PartitionStorageDiagnostics
{
    private const int MaintenanceSweepFailedEvent = 2401;
    private const string MaintenanceSweepFailedMessage = "The periodic storage maintenance sweep failed; the next tick retries.";

    [LoggerMessage(EventId = MaintenanceSweepFailedEvent, Level = LogLevel.Warning, Message = MaintenanceSweepFailedMessage)]
    internal static partial void MaintenanceSweepFailed(ILogger logger, Exception failure);
}
