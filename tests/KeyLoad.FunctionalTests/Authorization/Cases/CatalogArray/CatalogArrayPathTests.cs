namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class CatalogArrayPathTests
{
    [Test]
    public Task EscapedArrayCatalogRefusesInferenceThenPersistedUseAndReadRepairPatchReplayAndTwoColdRemainHealthy()
        => CatalogArrayPathFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
