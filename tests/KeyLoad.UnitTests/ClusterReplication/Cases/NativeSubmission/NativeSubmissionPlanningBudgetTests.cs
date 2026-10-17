namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class NativeSubmissionPlanningBudgetTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task QueueRetryPreparationChargesOriginalBudgetBeforeChoicesThenPreservesHealthySignedDecisions(bool records)
        => NativeQueueRetryPlanningBudgetFlow.RunAsync(records, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OrdinaryDocumentPreappendAdmissionKeepsOriginalBudgetCancellationAndStoredIdentityConflict(bool records)
        => NativeDocumentAdmissionBudgetFlow.RunAsync(records, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task RetainedQueueFailureRemainsExactThroughBoundedPreparationAndOriginalReplay()
        => NativeQueueRetryPlanningBudgetFlow.RetainedFailureAsync(TestContext.Current!.Execution.CancellationToken);
}
