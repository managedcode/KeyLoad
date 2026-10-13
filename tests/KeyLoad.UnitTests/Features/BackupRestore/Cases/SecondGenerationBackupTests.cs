namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class SecondGenerationBackupTests
{
    [Test]
    public Task RestoredOwnerWithoutNewCheckpointProducesVerifiedBackupThenConflictAndTwoColdReopens()
        => SecondGenerationBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
