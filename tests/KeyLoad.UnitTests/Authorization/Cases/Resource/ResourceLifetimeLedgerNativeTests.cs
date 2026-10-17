namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ResourceLifetimeLedgerNativeTests
{
    [Test]
    public Task GenuineBoundQuerySharesOriginalRetentionAcrossChildrenBudgetRefusalPolicyRepairAndJoinedRelease()
        => SafeProjectorOperationDatabase.RunAsync(ResourceLifetimeLedgerNativeFlow.ExecuteAsync,
            TestContext.Current!.Execution.CancellationToken);
}
