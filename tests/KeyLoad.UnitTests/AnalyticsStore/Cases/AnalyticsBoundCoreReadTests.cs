namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsBoundCoreReadTests
{
    [Test]
    public Task ActualBoundRawAndAggregateRetainOriginalWorkAndCurrentLifetime()
        => AnalyticsBoundCoreFlow.HealthyAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task PersistedDenialRetainsNoLifetimeThenActualAuthorizedReadIsHealthy()
        => AnalyticsBoundCoreRefusals.AuthorityAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ActualLifetimeRetentionBytesRefuseBeforeRowsAndFreshOperationIsHealthy()
        => AnalyticsBoundCoreRefusals.ResultBytesAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task DifferentActualLeaseCannotReplaceOriginalBindingThenOriginalReadIsHealthy()
        => AnalyticsBoundCoreRefusals.BindingAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OriginalCallerCancellationPublishesNoRowsThenIndependentOriginalReadIsHealthy()
        => AnalyticsBoundCoreCancellation.CallerAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OriginalAbsoluteDeadlineCannotBeRenewedByBoundCapture()
        => AnalyticsBoundCoreCancellation.DeadlineAsync(TestContext.Current!.Execution.CancellationToken);
}
