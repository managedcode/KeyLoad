namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityProtocol
{
    internal const long RepairEpoch = 3;
    internal const long RevokedEpoch = 4;
    internal const long ExpiredEpoch = 5;
    internal const long FinalEpoch = 6;
    internal const string MissingRepairResult = "Original acknowledged repair result is absent.";
    internal const string ColdScenario = "discovered-session-current-policy-cold";
}
