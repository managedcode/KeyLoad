namespace KeyLoad.FunctionalTests.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel(KeyLoad.FunctionalTests.OrleansExecution.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class RequestCqrsFatalSettlementTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public async Task AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence(fixture, fatal);
        }

        await RequestCqrsFatalSettlementCases.AssertOrdinaryCleanupFailuresRemainObservable(fixture);
        await RequestCqrsFatalSettlementCases.AssertFatalActivationWinsOverOrdinaryPrimaryAsync(fixture);
    }

    [Test]
    public async Task AcCrs003PullFatalNeverBecomesFailedAndJoinsProducer()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003PullFatalNeverBecomesFailedAndJoinsProducer(fixture, fatal);
        }
    }

    [Test]
    public async Task AcCrs003DisposalFatalSettlesBeforeActivation()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003DisposalFatalSettlesBeforeActivation(fixture, fatal);
        }
    }

    [Test]
    public async Task AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable(fixture, fatal);
        }
    }
}
