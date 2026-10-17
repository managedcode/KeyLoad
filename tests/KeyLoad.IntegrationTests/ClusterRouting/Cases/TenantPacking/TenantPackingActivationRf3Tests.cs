namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TenantPackingActivationRf3Tests
{
    [Test]
    public async Task PersistedTwoTenantPackingPreservesScopedReceiptsThroughNativeMigrationAndColdRepair()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await TenantPackingActivationRf3Scenario.RunAsync(caller.Token);
    }
}
