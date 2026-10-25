using P = KeyLoad.ClusterTests.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.ClusterTests.OrleansExecution;

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
