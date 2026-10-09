namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreUnsafeDestinationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcBackup002CliLeafAndAncestorLinksRefuseBeforePublicationThenSameDestinationRestoresCold(bool ancestor)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(fixture =>
            CliBackupRestoreUnsafeDestinationTrial.RunAsync(options, fixture, ancestor, cancellationToken), cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}
