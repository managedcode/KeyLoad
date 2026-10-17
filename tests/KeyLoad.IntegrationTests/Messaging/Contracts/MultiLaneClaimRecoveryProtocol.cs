namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneClaimRecoveryProtocol
{
    internal const int FirstLane = 0;
    internal const int SecondLane = 1;
    internal const int LaneCount = 2;
    internal const int SingleMutation = 1;
    internal const int FirstAttempt = 1;
    internal const int InitialVersion = 1;
    internal const int ClaimedVersion = 2;
    internal const int SettledVersion = 3;
    internal const int PlacementVersion = 1;
    internal const long InitialReadySequence = 1;
    internal const long HealthyReadySequence = 2;
    internal const long MinimumPosition = 0;
    internal const string HealthyMessage = "healthy";
    internal const string HealthyPayload = "{\"work\":2}";
    internal const string HealthyHeaders = "{\"kind\":\"next\"}";
}
