namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueDeadLetterSelectedOperation
{
    Read,
    Cancel,
    Redrive
}

internal enum QueueDeadLetterSelectedFault
{
    IndexAlias,
    MetadataAlias,
    WitnessMetadataAlias,
    MissingBody,
    ForeignBody
}

internal static class QueueDeadLetterSelectedProtocol
{
    internal const string HistoryQueue = "selected-dlq-history";
    internal const string HistoryMessage = "retained-inbox";
    internal const string Queue = "selected-dlq";
    internal const string Collection = "selected-dlq-effects";
    internal const string Administrator = "selected-dlq-admin";
    internal const string Original = "a-original";
    internal const string Foreign = "b-foreign";
    internal const string Healthy = "c-healthy";
    internal const string Payload = "{\"data\":\"canonical retained payload\"}";
    internal const string Headers = "{\"trace\":\"canonical retained headers\"}";
    internal const string Handler = "selected-dlq-handler";
    internal const string Body = "message-body";
    internal const string Metadata = "message-meta";
    internal const string Counters = "queue-counters";
    internal const string DeadLetter = "dead-letter";
    internal const string Parked = "queue-dead-letter-order";
    internal const string Pending = "queue-pending-dead-letter";
    internal const string Inbox = "inbox";
    internal const string Ready = "ready";
    internal const string Scheduled = "scheduled";
    internal const string Lease = "lease";
    internal const string Exhausted = "AttemptsExhausted";
    internal const string MissingReference = "The queue lifecycle reference does not match its metadata.";
    internal const string MissingMetadataDetail = "The queue lifecycle metadata is unavailable.";
    internal const string MissingBodyDetail = "The queue lifecycle body is unavailable.";
    internal const string ReturnedRefusal = "The native operation returned instead of refusing damaged authority.";
    internal const string RedriveKind = "redriveQueueMessage";
    internal const string CancelKind = "cancelQueueMessage";
    internal const string PutKind = "putDocument";
    internal const string Separator = ":";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int LeaseSeconds = 30;
}
