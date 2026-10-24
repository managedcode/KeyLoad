namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionContinuationProtocol
{
    internal const string Subject = "root";
    internal const string PublishMutation = "publishTopic";
    internal const string SourceFamily = "topic-event";
    internal const string SourceHeadFamily = "topic-head";
    internal const string SourceIdentityFamily = "topic-event-id";
    internal const string SourceSequenceFamily = "event-sequence";
    internal const int SingleEventCount = 1;
    internal const int ScanLookahead = 1;
    internal const long NackedPosition = 1;
    internal const long RetainedFloor = 1;
    internal const string GroupFamily = "subscription";
    internal const string StaleDetail = "The subscription delivery lease is stale.";
    internal const string MissingClaim = "The genuine settled claim outcome disappeared.";
    internal const string Topic = "topic";
    internal const string Purpose = "subscription-delivery";
    internal const string HealthyId = "event4";
    internal const string EventType = "Created";
    internal const string HealthyPayload = "{\"n\":4}";
    internal const string ByteBudgetDetail = "The next subscription event exceeds the receive byte budget.";
    internal const string MissingPending = "The genuine pending version row is missing.";
    internal const string MissingByteOutcome = "The earlier genuine byte-budget refusal disappeared.";
    internal const int FirstDelivery = 0;
    internal const int SecondDelivery = 1;
    internal const int ThirdDelivery = 2;
    internal const int FirstRepeat = 0;
    internal const long NoBytes = 0;
    internal const long InitialCheckpoint = 0;
    internal const long Generation = 1;
    internal const long OwnershipEpoch = 1;
    internal const long PolicyEpoch = 1;
    internal const int Window = 3;
    internal const long OriginalTail = 3;
    internal const long HealthyPosition = 4;
    internal const int InitialAttempt = 1;
    internal const long InitialLeaseVersion = 1;
    internal const int ReclaimedAttempt = 2;
    internal const long ReclaimedVersion = 2;
    internal const int LeaseSeconds = 30;
}
