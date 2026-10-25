namespace KeyLoad.FunctionalTests.Messaging;

internal static class BudgetedQueueReceiveOutcomeProtocol
{
    internal const string Root = "root";
    internal const string Worker = "budgeted-receive-worker";
    internal const string Queue = "budgeted-receive-queue";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Path = "/secret";
    internal const string Classification = "private";
    internal const string BeforeGrant = "queue-secret-before";
    internal const string AfterGrant = "queue-secret-after";
    internal const string Payload = "{\"public\":\"visible\",\"secret\":\"private-payload\"}";
    internal const string Headers = "{\"trace\":\"visible\",\"secret\":\"private-header\"}";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const string Refusal = "The current resource policy protects data retained in the original delivery.";
    internal const long Initial = 1;
    internal const long Step = 1;
    internal const int OneMessage = 1;
    internal const int ConstrainedResultBytes = 1;
    internal const long Acknowledged = 3;
    internal static readonly TimeSpan OriginalDeadline = TimeSpan.FromMilliseconds(25);
}
