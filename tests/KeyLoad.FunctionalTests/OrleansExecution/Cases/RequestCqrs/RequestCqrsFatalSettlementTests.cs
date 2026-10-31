namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class RequestCqrsFatalSettlementTests
{
    [Test]
    public async Task AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003FatalCreateFailureSettlesActivationAndKeepsFatalPrecedence(fatal);
        }

        await RequestCqrsFatalSettlementCases.AssertOrdinaryCleanupFailuresRemainObservable();
        await RequestCqrsFatalSettlementCases.AssertFatalActivationWinsOverOrdinaryPrimaryAsync();
    }

    [Test]
    public async Task AcCrs003PullFatalNeverBecomesFailedAndJoinsProducer()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003PullFatalNeverBecomesFailedAndJoinsProducer(fatal);
        }
    }

    [Test]
    public async Task AcCrs003DisposalFatalSettlesBeforeActivation()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003DisposalFatalSettlesBeforeActivation(fatal);
        }
    }

    [Test]
    public async Task AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003ActivationFatalAndOrdinaryCleanupFaultsRemainObservable(fatal);
        }
    }
}
