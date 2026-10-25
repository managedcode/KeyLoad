namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class ResourceLifetimeLedgerTests
{
    [Test]
    public Task GenuineBoundQuerySharesOriginalRetentionAcrossChildrenBudgetRefusalPolicyRepairAndJoinedRelease()
        => SafeProjectorOperationDatabase.RunAsync(ResourceLifetimeLedgerFlow.ExecuteAsync,
            TestContext.Current!.Execution.CancellationToken);
}
