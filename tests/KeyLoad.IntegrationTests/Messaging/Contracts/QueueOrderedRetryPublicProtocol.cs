namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicProtocol
{
    internal const string First = "ordered-a";
    internal const string Second = "ordered-b";
    internal const string Healthy = "ordered-c";
    internal const int Initial = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int Seven = 7;
    internal const int Eight = 8;
    internal const int Ten = 10;
    internal const string ColdScenario = "ordered-retry-same-owner-cold";
}
