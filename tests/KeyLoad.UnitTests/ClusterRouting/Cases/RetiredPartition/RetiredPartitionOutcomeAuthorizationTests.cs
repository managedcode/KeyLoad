namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RetiredPartitionOutcomeAuthorizationTests
{
    [Test]
    public Task ActualRetirementColdOwnerRevocationAndRestorationKeepOriginalEpochBoundOutcome()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            await RetiredPartitionOutcomeAuthorizationTrial.ExecuteAsync(source, target);
        });
}
