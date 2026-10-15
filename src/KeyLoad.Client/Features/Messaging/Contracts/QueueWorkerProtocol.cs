namespace KeyLoad.Client;

internal static class QueueWorkerProtocol
{
    internal const int InitialRetryIndex = 0;
    internal const int NoPriorRenewals = 0;
    internal const int NoPriorReceives = 0;
    internal const int EmptyCapacity = 0;
    internal const string InvalidReceiveId = "A nonempty original queue worker receive ID is required.";
    internal const string InvalidObservation = "A queue worker reconciliation must retain exactly one original native operation.";
    internal const string Unknown = nameof(ErrorCode.UnknownWriteOutcome);
}
