using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkSignedMinimumWholeFlowTests
{
    [Test]
    public Task MinimumPayloadMacRefusalFreshGrantRepairOriginalReceiptsAndTwoColdOwners()
        => ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, deadline.Token);
            await scenario.InitializeAsync(caller.Token);
            deadline.CancelAfter(scenario.Timing.CompletionTimeout);
            await SampleChunkSignedMinimumFlow.RunAsync(scenario, caller.Token);
        });
}
