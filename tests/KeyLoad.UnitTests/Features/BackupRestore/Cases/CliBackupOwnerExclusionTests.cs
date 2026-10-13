namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupOwnerExclusionTests
{
    [Test]
    public Task ActualCliBackupRefusesOpenNativeOwnerThenRestoresAfterJoinedCloseAndTwoColdReopens()
        => CliBackupOwnerExclusionTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
