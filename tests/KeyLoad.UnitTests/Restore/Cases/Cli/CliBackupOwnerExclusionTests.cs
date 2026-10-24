namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class CliBackupOwnerExclusionTests
{
    [Test]
    public Task ActualCliBackupRefusesOpenOwnerThenRestoresAfterJoinedCloseAndTwoColdReopens()
        => CliBackupOwnerExclusionTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
