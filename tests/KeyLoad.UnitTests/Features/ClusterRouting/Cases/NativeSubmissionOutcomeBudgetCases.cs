namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSubmissionOutcomeBudgetCases
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualRepeatedOriginalReadsRefuseBeforeCallbackWithoutEffectsAndChildRemainsHealthy(bool owned)
        => C1OutcomeInspectionFixture.RunOwnedAsync(fixture => NativeSubmissionOutcomeBudgetFlow.RepeatedAsync(fixture, owned));

    [Test]
    public Task ActualOversizeOriginalValueRefusesBeforeCallbackWithoutEffectsAndRestoredChildIsHealthy()
        => C1OutcomeInspectionFixture.RunOwnedAsync(NativeSubmissionOutcomeBudgetFlow.OversizeAsync);
}
