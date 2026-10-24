namespace KeyLoad.UnitTests.Messaging;

internal static class QueueAccountingFaultProtocol
{
    internal const string Queue = "accounting-jobs";
    internal const string Collection = "accounting-effects";
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Document = "derived";
    internal const string Handler = "accounting-handler";
    internal const string Root = "root";
    internal const string Payload = "{\"data\":\"retained\"}";
    internal const string Headers = "{\"trace\":\"original\"}";
    internal const string HealthyPayload = "{\"data\":\"healthy\"}";
    internal const string EmptyHeaders = "{}";
    internal const string CounterSpace = "queue-counters";
    internal const string BodySpace = "message-body";
    internal const string RetryReason = "RetryRequested";
    internal const string PutKind = "putDocument";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int ScheduledSeconds = 5;
    internal const int ExpirySeconds = 10;
    internal const int ExpiredEvaluationSeconds = 20;
    internal const int LeaseSeconds = 30;
    internal const int ReclaimEvaluationSeconds = 31;
    internal const int RetryMilliseconds = 1_000;
}
