namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AdversarialAuthorityProtocol
{
    internal const string PutKind = "putDocument";
    internal const string ColdScenario = "forged-authority-current-cold";
    internal const int RestrictedEpoch = 2;
    internal const int RevokedEpoch = 3;
    internal const int RepairedEpoch = 4;
}
