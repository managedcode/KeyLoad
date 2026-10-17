using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class PackedBackupNativeCutHealthy
{
    private const string Restore = "restore";

    internal static async Task RunAsync(CliBackupRestoreFixture fixture, string unpacked, string target,
        string cliTarget, Guid incarnation, byte[] key, long position, bool containsSecond, CancellationToken token)
    {
        var identity = ZoneTreeStore.Restore(unpacked, target, UnitExecutionOptions.StorageExecution(), incarnation, key);
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.OriginalIdentity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(key)).IsTrue();
        await Assert.That(identity.Durability).IsEqualTo(fixture.OriginalIdentity.Durability);
        await Assert.That(identity.DispatchPaused).IsTrue();
        await PackedBackupNativeCutCold.RunAsync(target, identity, position, containsSecond, token);
        var cli = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [Restore, unpacked, cliTarget], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(cli);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, cli, cliTarget);
        var cliIdentity = NativeBackupJournalRepairAssertions.Read(cliTarget, token).Identity;
        await PackedBackupNativeCutCold.RunAsync(cliTarget, cliIdentity, position, containsSecond, token);
    }
}
