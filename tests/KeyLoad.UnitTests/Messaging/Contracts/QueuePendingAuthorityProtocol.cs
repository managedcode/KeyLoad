namespace KeyLoad.UnitTests.Messaging;

internal static class QueuePendingAuthorityProtocol
{
    internal const string History = "c-retained-inbox";
    internal const string Healthy = "d-pending-healthy";
    internal const string Handler = "pending-authority-handler";
    internal const string Inbox = "inbox";
    internal const string Order = "queue-order";
    internal const string Exhausted = "AttemptsExhausted";
    internal const string MissingReference = "The queue lifecycle reference does not match its metadata.";
    internal const string ParkKind = "parkPendingQueueMessage";
    internal const string PutKind = "putDocument";
}
