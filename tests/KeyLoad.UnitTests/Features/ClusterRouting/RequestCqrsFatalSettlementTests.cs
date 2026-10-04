namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel]
internal sealed class RequestCqrsFatalSettlementTests(RequestCqrsClusterFixture fixture)
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
    public async Task AcCrs003NativePullFatalNeverBecomesFailedAndJoinsProducer()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003NativePullFatalNeverBecomesFailedAndJoinsProducer(fixture, fatal);
        }
    }

    [Test]
    public async Task AcCrs003NativeDisposalFatalSettlesBeforeActivation()
    {
        foreach (var fatal in RequestCqrsFatalSettlementProbe.FatalCases())
        {
            await RequestCqrsFatalSettlementCases.AcCrs003NativeDisposalFatalSettlesBeforeActivation(fixture, fatal);
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
