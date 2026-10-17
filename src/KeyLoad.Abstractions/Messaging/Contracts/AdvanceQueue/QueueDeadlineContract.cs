namespace KeyLoad;

internal static class QueueDeadlineContract
{
    internal const string Alias = "keyload.mutation.advance-queue-deadline.v1";
    internal const string KindAlias = "keyload.messaging.queue-deadline-kind.v1";
    internal const string Discriminator = "advanceQueueDeadline";
    internal const string KindJsonName = "deadlineKind";
    internal const int QueueField = 0;
    internal const int MessageField = 1;
    internal const int StateField = 2;
    internal const int VersionField = 3;
    internal const int LeaseField = 4;
    internal const int DeadlineField = 5;
    internal const int KindField = 6;
}
