namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Active invalid unclaimed controls cannot block genuine original owner disposal or authorize effects.</summary>
[NotInParallel]
internal sealed class PartitionMovementActiveAdjunctRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualActiveWrongRequestOrDuplicateAdjunctFailsThenOriginalOwnerDisposesAndColdAbortFreshMoveCompletes(bool duplicate)
        => PartitionMovementActiveAdjunctRf3Trial.RunAsync(duplicate, TestContext.Current!.Execution.CancellationToken);
}
