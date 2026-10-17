namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixProtocol
{
    internal const string ScheduledMessage = "scheduled-input";
    internal const string LeasedMessage = "leased-input";
    internal const string ExpiringMessage = "expiring-input";
    internal const string Root = "root";
    internal const string Json = "{}";
    internal const string Metadata = "message-meta";
    internal const string Body = "message-body";
    internal const string Ready = "ready";
    internal const string Lease = "lease";
    internal const string Scheduled = "scheduled";
    internal const string Counters = "queue-counters";
    internal const string EnqueueReceipt = "enqueue";
    internal const string AckReceipt = "Ack";
    internal const char PoisonCharacter = 'x';
    internal const string ByteSeparator = ":";
    internal const string IncompleteImage = "The complete deadline fixture prefix exceeded its independent image bound.";
    internal const string NoProgress = "Original deadline work did not progress within its bounded sweep.";
    internal const byte PoisonByte = 0x01;
    internal const int Initial = 0;
    internal const int First = 1;
    internal const int Second = 2;
    internal const int FamilyCount = 3;
    internal const int ExpiredLeaseStateVersion = 3;
    internal const int FirstAdvancedReadySequence = 3;
    internal const int FinalReadySequence = 4;
    internal const int LeaseSeconds = 30;
    internal const int ImageRecordBound = 4096;
}
