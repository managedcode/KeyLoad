namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class TruncatedBackupArchiveTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalTruncatedArchiveRefusesPublicationThenSamePathRepairRestoresAndTwoCold(bool emptyDestination)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var root = await CliBackupRestoreFixture.RunAsync(
            fixture => TruncatedBackupArchiveFlow.RunAsync(options, fixture, emptyDestination, token), token);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}
