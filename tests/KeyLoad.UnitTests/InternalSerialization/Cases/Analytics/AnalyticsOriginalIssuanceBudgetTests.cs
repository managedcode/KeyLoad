namespace KeyLoad.UnitTests.InternalSerialization;

internal sealed class AnalyticsOriginalIssuanceBudgetTests
{
    [Test]
    [Arguments(AnalyticsIdentityBudgetFault.SpentBytes)]
    [Arguments(AnalyticsIdentityBudgetFault.SpentRecords)]
    [Arguments(AnalyticsIdentityBudgetFault.Cancelled)]
    [Arguments(AnalyticsIdentityBudgetFault.Expired)]
    [Arguments(AnalyticsIdentityBudgetFault.IdentityOutput)]
    [Arguments(AnalyticsIdentityBudgetFault.MissingOriginalWork)]
    public Task OriginalAnalyticsIssuanceWorkRefusesBeforePublicationAndHealthyNewAttemptCommits(AnalyticsIdentityBudgetFault fault)
        => AnalyticsOriginalIssuanceBudgetFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
