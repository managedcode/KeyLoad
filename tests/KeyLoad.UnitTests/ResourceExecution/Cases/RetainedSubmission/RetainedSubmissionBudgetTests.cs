namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class RetainedSubmissionBudgetTests
{
    [Test]
    public Task RetainedOutcomeChargesOriginalWorkAndPreservesCurrentAuthority()
        => RetainedSubmissionBudgetFlow.RunAsync();
}
