using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class DropTableLifecycleRf3Tests
{
    [Test]
    public Task PublicDropRecreateRetainsAuthorityAndReplayAcrossTwoColdRf3Waves()
        => DropTableLifecycleRf3Flow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
