using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeSignedPurposeOperationTests
{
    [Test]
    public Task OriginalSignedPurposeRefusesBeforeRoutingThenSameOwnerRepairReplayAndTwoColdCuts()
    {
        var observation = new NativeSignedPurposeObservation();
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            await NativeSignedPurposeOperation.RunAsync(scenario, observation, timeout.Token);
        }, purposeObserver: observation);
    }
}
