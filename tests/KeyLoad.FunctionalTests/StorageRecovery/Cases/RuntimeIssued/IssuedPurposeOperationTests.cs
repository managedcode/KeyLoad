using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class IssuedPurposeOperationTests
{
    [Test]
    public Task OriginalIssuedPurposeRefusesBeforeRoutingThenSameOwnerRepairReplayAndTwoColdCuts()
    {
        var observation = new IssuedPurposeObservation();
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, deadline.Token);
            await scenario.InitializeAsync(caller.Token);
            deadline.CancelAfter(scenario.Timing.CompletionTimeout);
            await IssuedPurposeOperation.RunAsync(scenario, observation, caller.Token);
        }, purposeObserver: observation);
    }
}
