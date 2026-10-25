namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class SecondGenerationBackupTests
{
    [Test]
    public Task RestoredOwnerWithoutNewCheckpointProducesVerifiedBackupThenConflictAndTwoColdReopens()
        => SecondGenerationBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
