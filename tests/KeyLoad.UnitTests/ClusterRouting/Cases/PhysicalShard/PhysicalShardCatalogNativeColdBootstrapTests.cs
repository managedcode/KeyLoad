namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual Server catalog startup without a fixture-written physical catalog.</summary>
internal sealed class PhysicalShardCatalogNativeColdBootstrapTests
{
    [Test]
    public Task UnseededServerOwnersBootstrapCatalogThenReuseAuthorityAndReceiptsAcrossTwoColdStarts()
        => PhysicalShardCatalogNativeColdBootstrapFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
