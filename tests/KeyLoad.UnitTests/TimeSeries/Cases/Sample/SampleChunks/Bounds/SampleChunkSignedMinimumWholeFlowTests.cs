using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkSignedMinimumWholeFlowTests
{
    [Test]
    public Task MinimumPayloadMacRefusalFreshGrantRepairOriginalReceiptsAndTwoColdOwners()
        => ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, deadline.Token);
            await SampleChunkSignedMinimumFlow.RunAsync(scenario, caller.Token);
        });
}
