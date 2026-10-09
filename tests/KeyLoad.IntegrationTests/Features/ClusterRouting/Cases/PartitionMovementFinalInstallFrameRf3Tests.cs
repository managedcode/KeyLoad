namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementFinalInstallFrameRf3Tests
{
    [Test]
    public Task ActualFinalInstallNativeFrameLegalAndOneByteShortRetainEffectsThenSameMoveColdHealthyContinuation()
        => PartitionMovementFinalInstallFrameRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
