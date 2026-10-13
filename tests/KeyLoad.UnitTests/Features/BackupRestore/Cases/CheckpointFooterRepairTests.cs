namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CheckpointFooterRepairTests
{
    [Test]
    public Task HashConsistentCheckpointFooterRefusalRepairsOriginalArchiveAndPreservesTwoColdCuts()
        => CheckpointFooterRepairTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
