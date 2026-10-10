namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryProtocol
{
    internal const string Queue = "ordered-retry";
    internal const string First = "ordered-a";
    internal const string Second = "ordered-b";
    internal const string Other = "ordered-c";
    internal const string Healthy = "ordered-d";
    internal const string SharedKey = "shared-key";
    internal const string OtherKey = "other-key";
    internal const string Payload = "{\"knowledge\":\"strict complete literal\"}";
    internal const string Headers = "{\"kind\":\"ordered-retry\"}";
    internal const string Root = "root";
    internal const int Initial = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int Seven = 7;
    internal const int Nine = 9;
    internal const string Expired = "ordered-expired";
    internal const string AfterExpiry = "ordered-after-expiry";
    internal const string ProfileHealthy = "profile-healthy";
    internal const string MissingKeyId = "missing-key";
}
