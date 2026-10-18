using P = KeyLoad.IntegrationTests.Features.ClusterRouting.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class CatalogBootstrapAuthorityClusterTests
{
    [Test]
    [Arguments(P.KeyRevoked)]
    [Arguments(P.PrincipalRevoked)]
    [Arguments(P.PrincipalDemoted)]
    public Task CurrentNativeCatalogAndOriginalReceiptsSurvivePublicBootstrapAuthorityRevocationAndTwoFullColdRestarts(string variant)
        => CatalogBootstrapAuthorityFlow.RunAsync(variant);
}
