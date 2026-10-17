namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class RetainedSubmissionBudgetTests
{
    [Test]
    public Task NativeRetainedOutcomeChargesOriginalWorkAndPreservesCurrentAuthority()
        => RetainedSubmissionBudgetFlow.RunAsync();
}
