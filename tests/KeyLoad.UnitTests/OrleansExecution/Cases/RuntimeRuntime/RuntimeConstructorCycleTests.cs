namespace KeyLoad.UnitTests.Features.OrleansExecution;

[NotInParallel]
internal sealed class RuntimeConstructorCycleTests
{
    [Test]
    public async Task ProductionConstructorsAndPendingCatalogCancelBeforePhysicalPublication()
        => await RuntimeConstructorCycleFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
