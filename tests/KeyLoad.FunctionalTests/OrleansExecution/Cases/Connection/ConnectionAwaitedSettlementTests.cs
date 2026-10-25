namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionAwaitedSettlementTests
{
    [Test]
    public Task ReusedConnectionAwaitsCleanupAcrossSuccessCancellationDisposalAndFailure()
    {
        ConnectionAwaitedSettlementObservation? observer = null;
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            using var parent = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var token = parent.Token;
            try
            {
                await scenario.InitializeAsync(token);
                var firstPrincipal = scenario.Principal();
                var nextPrincipal = scenario.Principal();
                await Assert.That(firstPrincipal.Id).IsNotEqualTo(nextPrincipal.Id);
                var success = await ConnectionAwaitedSettlementFlow.SuccessAsync(scenario, observer!, firstPrincipal, "awaited-first", token);
                var cancelled = await ConnectionAwaitedSettlementFlow.CancelAsync(scenario, observer!, nextPrincipal, token);
                var early = await ConnectionAwaitedSettlementFlow.EarlyAsync(scenario, observer!, firstPrincipal, token);
                var fault = await ConnectionAwaitedSettlementFlow.FaultAsync(scenario, observer!, nextPrincipal, token);
                var following = await ConnectionAwaitedSettlementFlow.SuccessAsync(scenario, observer!, firstPrincipal, "awaited-following", token);
                await ConnectionAwaitedSettlementAssertions.SameContextAsync(scenario, observer!, success, cancelled);
                await ConnectionAwaitedSettlementAssertions.SameContextAsync(scenario, observer!, cancelled, early);
                await ConnectionAwaitedSettlementAssertions.SameContextAsync(scenario, observer!, early, fault);
                await ConnectionAwaitedSettlementAssertions.SameContextAsync(scenario, observer!, fault, following);
                await Assert.That(scenario.Observation.Read(cancelled).Identity.PrincipalId).IsEqualTo(nextPrincipal.Id);
                await Assert.That(scenario.Observation.Read(early).Identity.PrincipalId).IsEqualTo(firstPrincipal.Id);
                await Assert.That(scenario.Observation.Read(fault).Identity.PrincipalId).IsEqualTo(nextPrincipal.Id);
                await scenario.CloseAsync(token);
            }
            finally { observer?.ReleaseAll(); }
        }, phaseObserver: original => observer = new(original));
    }
}
