using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleChunkMinimumEnvelopeWholeFlowTests
{
    [Test]
    public Task MinimumPayloadScopeRefusalFreshGrantRepairOriginalReceiptsAndTwoColdOwners()
        => ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, deadline.Token);
            await scenario.InitializeAsync(caller.Token);
            deadline.CancelAfter(scenario.Timing.CompletionTimeout);
            await SampleChunkMinimumEnvelopeFlow.RunAsync(scenario, caller.Token);
        });
}
