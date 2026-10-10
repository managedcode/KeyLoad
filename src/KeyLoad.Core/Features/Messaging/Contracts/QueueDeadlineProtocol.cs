namespace KeyLoad.Core.Features.Messaging;

internal static class QueueDeadlineProtocol
{
    internal const int Initial = 0;
    internal const int Step = 1;
    internal const string InvalidShape = "The queue deadline condition is invalid.";
    internal const string StaleCondition = "The queue deadline condition no longer matches.";
    internal const string NotDue = "The queue deadline has not been reached.";
    internal const string BatchRequired = "Queue deadline advancement requires an original direct batch operation.";
    internal const string ReceiptKind = "advanceQueueDeadline";
}
