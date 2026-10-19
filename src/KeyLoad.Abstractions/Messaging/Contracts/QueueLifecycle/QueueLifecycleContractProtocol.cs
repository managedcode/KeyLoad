namespace KeyLoad;

internal static class QueueLifecycleContractProtocol
{
    internal const int QueueField = 0;
    internal const int MessageField = 1;
    internal const int VersionField = 2;
    internal const int GenerationField = 3;
    internal const int NotBeforeField = 4;
    internal const string RedriveKind = "redriveQueueMessage";
    internal const string CancelKind = "cancelQueueMessage";
    internal const string ParkKind = "parkPendingQueueMessage";
    internal const string RedriveAlias = "keyload.contract.redrive-queue-message";
    internal const string CancelAlias = "keyload.contract.cancel-queue-message";
    internal const string ParkAlias = "keyload.contract.park-pending-queue-message";
}
