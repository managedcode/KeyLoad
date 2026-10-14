using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeCloseAdmissionTests
{
    [Test]
    public Task SignedCloseRetainsOriginalCallbackLeaseThenFreshOwnerAndSecondPrincipalReplaySucceed()
    {
        ConnectionAwaitedSettlementObservation? observer = null;
        return ConnectionNativeScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            try
            {
                await scenario.InitializeAsync(caller.Token);
                await ConnectionNativeCloseAdmissionFlow.RunAsync(scenario, observer!, caller.Token);
            }
            finally { observer?.ReleaseAll(); }
        }, new GrainRoutingOptions { MaximumConnectionOperations = 1 },
            phaseObserver: original => observer = new(original));
    }
}
