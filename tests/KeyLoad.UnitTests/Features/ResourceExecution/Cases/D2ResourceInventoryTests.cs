namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class D2ResourceInventoryTests
{
    [Test]
    public async Task ConservativeNativeInventoryPreservesFullRowsFreshPolicyOriginalReceiptsBudgetsAndTwoColdOwners()
        => await D2ResourceInventoryFlow.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}
