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
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            await SignedPurposeOperation.RunAsync(scenario, observation, timeout.Token);
        }, purposeObserver: observation);
    }
}
