using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class CurrentJournalSignatureHealthy
{
    private const string Restore = "restore";
    private const long CapturedPosition = 2L;

    internal static async Task RunAsync(BackupCutFixture fixture, CurrentJournalSignatureAuthority authority,
        string cliTarget, CancellationToken token)
    {
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(CapturedPosition);
        await Assert.That(JsonSerializer.Serialize(verified.Identity, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(fixture.Identity, JsonDefaults.Options));
        var identity = authority.Restore(fixture.Backup, fixture.Destination);
        await authority.RequireAsync(identity, fixture.Identity);
        await CurrentJournalSignatureCold.RunAsync(fixture, fixture.Destination, identity, token);
        var cli = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [Restore, fixture.Backup, cliTarget], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(cli);
        var restored = BackupJournalRepairAssertions.Read(cliTarget, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(cli, restored.Identity);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await CurrentJournalSignatureCold.RunAsync(fixture, cliTarget, restored.Identity, token);
    }
}
