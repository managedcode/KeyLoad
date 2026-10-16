namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsEntityLinkLifetimeTests
{
    [Test]
    public Task IncomingAnalyticsLinksPersistActualBoundIdentityAndRefuseForeignResourceIncarnation()
        => AnalyticsEntityLinkLifetimeFlow.IncomingAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task DeliberateStoredAnalyticsLinkCorruptionFailsClosedUntilExactOwnedRowRepair(bool foreign)
        => AnalyticsEntityLinkLifetimeFlow.StoredCorruptionAsync(foreign, TestContext.Current!.Execution.CancellationToken);
}
