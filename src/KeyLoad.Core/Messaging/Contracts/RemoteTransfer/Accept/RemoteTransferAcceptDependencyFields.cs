namespace KeyLoad.Core.Messaging;

internal static class RemoteTransferAcceptDependencyFields
{
    internal const uint PrincipalPolicyEpoch = 0;
    internal const uint FieldHeaderPolicyDigest = 1;
    internal const uint QueueMaxStoredMessages = 2;
    internal const uint QueueMaxStoredBytes = 3;
    internal const uint QueueStoredMessages = 4;
    internal const uint QueueStoredBytes = 5;
    internal const uint TransferStoredRecords = 6;
    internal const uint TransferStoredBytes = 7;
    internal const uint MaxBatchBytes = 8;
    internal const uint MaxScanRecords = 9;
    internal const string Alias = "keyload.core.queue-transfer.accept-dependency";
}
