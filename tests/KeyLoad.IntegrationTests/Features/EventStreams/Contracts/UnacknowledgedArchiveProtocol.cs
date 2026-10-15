namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveProtocol
{
    internal const string Topic = "unacked-archive-topic";
    internal const string Group = "unacked-archive-group";
    internal const string EventType = "Created";
    internal const string PublishKind = "publishTopic";
    internal const string PurgeKind = "purgeTopic";
    internal const string AckKind = "Ack";
    internal const string PinDetail = "The topic purge cut is pinned by a subscription checkpoint.";
    internal const string SafeCapacity = "The operation exceeds the available resource capacity.";
    internal const string Missing = "The original unacked archive state is absent.";
    internal const int Version = 1;
    internal const long InitialGeneration = 1;
    internal const long RepairedGeneration = 2;
    internal const int ThirdDelivery = 2;
    internal const int FirstDelivery = 0;
    internal const int SecondDelivery = 1;
    internal const int ClaimedEvents = 3;
    internal const long BeforeFirst = 0;
    internal const long FirstPosition = 1;
    internal const long PurgeThrough = 2;
    internal const string PurgeThroughText = "2";
    internal const long ThirdPosition = 3;
    internal const long HealthyTail = 4;
    internal const int HealthyRows = 2;
    internal const int RetainedRow = 0;
    internal const int AddedRow = 1;
    internal const int FirstAttempt = 1;
    internal const long OriginalLeaseVersion = 1;
    internal const long PositivePositionBoundary = 0;
    internal const string ThirdEventId = "pin-event3";
    internal const string HealthyEventId = "pin-event4";
    internal const string SafeFailure = "The database operation could not be completed.";
    internal const string InvalidSignedToken = "The signed token is invalid.";
    internal const string EarlierIncarnation = "The command belongs to an earlier incarnation.";
    internal const string StaleReceive = "The subscription delivery lease is stale.";
    internal const string HistoryUnavailable = "The requested event history was retained away.";
    internal static EventData[] Events() =>
        [new("pin-event1", EventType, "{\"n\":1}"), new("pin-event2", EventType, "{\"n\":2}"),
         new("pin-event3", EventType, "{\"n\":3}")];
    internal static EventData Healthy() => new("pin-event4", EventType, "{\"n\":4}");
}
