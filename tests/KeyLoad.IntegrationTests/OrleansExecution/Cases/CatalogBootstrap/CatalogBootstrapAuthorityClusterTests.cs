using P = KeyLoad.IntegrationTests.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.OrleansExecution;

[NotInParallel]
internal sealed class CatalogBootstrapAuthorityClusterTests
{
    [Test]
    [Arguments(P.KeyRevoked)]
    [Arguments(P.PrincipalRevoked)]
    [Arguments(P.PrincipalDemoted)]
    public Task CurrentCatalogAndOriginalReceiptsSurvivePublicBootstrapAuthorityRevocationAndTwoFullColdRestarts(string variant)
        => CatalogBootstrapAuthorityFlow.RunAsync(variant);
}
