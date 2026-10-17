namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueLeaseAuthorityOperation
{
    Ack,
    Nack,
    Renew,
    Processing
}

internal enum QueueLeaseAuthorityFault
{
    MissingReference,
    ForeignReference,
    ForeignBodyIdentity,
    ForeignMetadataIdentity,
    MissingMetadataDeadline,
    NegativeAttempts,
    ZeroAttempts,
    NegativeStateVersion,
    ZeroStateVersion,
    UnclaimedStateVersion,
    NegativeReadySequence,
    ZeroReadySequence
}

internal static class QueueLeaseAuthorityProtocol
{
    internal const string Root = "root";
    internal const string Queue = "lease-authority-jobs";
    internal const string Collection = "lease-authority-effects";
    internal const string Message = "original";
    internal const string Foreign = "foreign";
    internal const string Healthy = "healthy";
    internal const string Document = "derived";
    internal const string Handler = "lease-authority-handler";
    internal const string Payload = "{\"data\":\"retained\"}";
    internal const string Headers = "{\"trace\":\"retained\"}";
    internal const string HealthyPayload = "{\"data\":\"healthy\"}";
    internal const string EmptyHeaders = "{}";
    internal const string BodySpace = "message-body";
    internal const string MetadataSpace = "message-meta";
    internal const string LeaseSpace = "lease";
    internal const string ReadySpace = "ready";
    internal const string ScheduledSpace = "scheduled";
    internal const string CounterSpace = "queue-counters";
    internal const string InboxSpace = "inbox";
    internal const string PutKind = "putDocument";
    internal const string RetryReason = "RetryRequested";
    internal const string InvalidMetadataDetail = "The retained queue lease metadata is invalid.";
    internal const string MissingBodyDetail = "A leased message body is absent.";
    internal const string RowSeparator = ":";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int LeaseSeconds = 30;
    internal const int RetryMilliseconds = 1_000;
}
