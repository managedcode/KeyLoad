namespace KeyLoad.UnitTests.Messaging;

internal static class QueueEnqueueAuthorityProtocol
{
    internal const string SequenceOriginal = "original";
    internal const string OrderingKey = "group";
    internal const string InvalidOrderingKey = "\n";
    internal const string SequenceHealthyJson = "{\"work\":2}";
    internal const string LeaseFamily = "lease";
    internal const string OrderFamily = "queue-order";
    internal const string ExpectedSequenceDetail = "The queue enqueue sequence is exhausted.";
    internal const string ExpectedMissingKeyDetail = "Strict queue ordering requires an ordering key.";
    internal const string ExpectedIdentifierDetail = "An identifier is missing, too long or contains control characters.";
    internal const string MissingOwnedRow = "The queue fixture must own its original retained row.";
    internal const int Initial = 0;
    internal const int First = 1;
    internal const int Second = 2;
    internal const int Settled = 3;
    internal const long FirstLong = 1;
}
