namespace KeyLoad.UnitTests;

internal sealed class NativeSubmissionJournalBudgetCases
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualCanonicalAndReplicaReadsShareOneOwnerBudget(bool exhaustCanonical)
        => NativeSubmissionJournalBudgetFlow.RunOwnedAsync(exhaustCanonical);
}
