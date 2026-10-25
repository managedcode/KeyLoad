namespace KeyLoad.FunctionalTests.Authorization;

/// <summary>Local real-store credential/owner budget proof; not a execution transport or CLUSTER acknowledgement.</summary>
internal sealed class CredentialFrameBudgetTests
{
    [Test]
    public Task CredentialAndOwnerReadsShareOriginalBudgetAndObservePersistedRevocation()
        => CredentialFrameBudgetFlow.RunAsync();
}
