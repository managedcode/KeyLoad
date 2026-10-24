namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class ConnectionCloseAdmissionTests
{
    [Test]
    public Task SignedCloseRetainsOriginalCallbackLeaseThenFreshOwnerAndSecondPrincipalReplaySucceed()
    {
        ConnectionAwaitedSettlementObservation? observer = null;
        return ConnectionScenario.RunAsync(async scenario =>
        {
            try
            {
                await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
                using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
                using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                    TestContext.Current!.Execution.CancellationToken);
                await ConnectionCloseAdmissionFlow.RunAsync(scenario, observer!, caller.Token);
            }
            finally { observer?.ReleaseAll(); }
        }, phaseObserver: original => observer = new(original));
    }
}
