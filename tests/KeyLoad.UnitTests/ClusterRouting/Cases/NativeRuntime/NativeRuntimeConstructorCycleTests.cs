namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeRuntimeConstructorCycleTests
{
    [Test]
    public async Task ProductionConstructorsAndPendingNativeCatalogCancelBeforePhysicalPublication()
        => await NativeRuntimeConstructorCycleFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
