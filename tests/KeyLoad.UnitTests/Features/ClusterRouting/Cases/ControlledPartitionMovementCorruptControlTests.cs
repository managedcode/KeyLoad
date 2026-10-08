namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises native fatal corrupted control and original-operation repair with actual cold owners.</summary>
[NotInParallel]
internal sealed class ControlledPartitionMovementCorruptControlTests
{
    [Test]
    public Task MalformedPersistedNoneControlRejectsOriginalApplyAndRepairedColdOwnersInstallExactly()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(ControlledPartitionMovementNoneControlTrial.ExecuteAsync);
}
