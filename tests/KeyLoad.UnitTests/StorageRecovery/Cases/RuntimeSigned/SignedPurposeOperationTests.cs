using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class SignedPurposeOperationTests
{
    [Test]
    public Task OriginalSignedPurposeRefusesBeforeRoutingThenSameOwnerRepairReplayAndTwoColdCuts()
    {
        var observation = new SignedPurposeObservation();
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current!.Execution.CancellationToken, deadline.Token);
            await scenario.InitializeAsync(caller.Token);
            deadline.CancelAfter(scenario.Timing.CompletionTimeout);
            await SignedPurposeOperation.RunAsync(scenario, observation, caller.Token);
        }, purposeObserver: observation);
    }
}
