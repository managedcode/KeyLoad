namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityProtocol
{
    internal const long RepairEpoch = 3;
    internal const long RevokedEpoch = 4;
    internal const long FinalEpoch = 5;
    internal const string MissingRepairResult = "Original acknowledged repair result is absent.";
    internal const string ColdScenario = "kl068-discovered-session-current-policy-cold";
}
