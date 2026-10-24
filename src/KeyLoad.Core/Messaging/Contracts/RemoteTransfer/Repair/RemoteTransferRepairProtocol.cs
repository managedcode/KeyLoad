namespace KeyLoad.Core.Messaging;

internal static class RemoteTransferRepairProtocol
{
    internal const long InitialGeneration = 1;
    internal const int EmptyHistory = 0;
    internal const string Purpose = "keyload.queue-transfer.current-authority-repair";
    internal const string ReadPurpose = "keyload.queue-transfer.repair-failure-read";
    internal const string AcceptDomain = "keyload.queue-transfer.accept-policy-command";
    internal const string CompleteDomain = "keyload.queue-transfer.complete-repair-command";
    internal const string AdvanceDomain = "keyload.queue-transfer.advance-repair-command";
    internal const string Invalid = "The execution queue transfer repair identity is invalid.";
    internal const string Stale = "The queue transfer repair generation or current authority is stale.";
    internal const string Unavailable = "The execution queue transfer repair is unavailable.";
    internal const string Capacity = "The retained queue transfer repair capacity is exhausted.";
    internal const string AdvanceKind = "queueTransferRepairAdvanced";
}
