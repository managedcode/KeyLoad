namespace KeyLoad.UnitTests.Features.Authorization;

/// <summary>Local real-store credential/owner budget proof; not a native transport or CLUSTER acknowledgement.</summary>
internal sealed class NativeCredentialFrameBudgetTests
{
    [Test]
    public Task CredentialAndOwnerReadsShareOriginalBudgetAndObservePersistedRevocation()
        => NativeCredentialFrameBudgetFlow.RunAsync();
}
