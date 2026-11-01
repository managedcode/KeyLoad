using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class ZoneTreeBackupAdmissionTests
{
    [Test]
    public async Task RestoreRejectsInvalidAuthorityBeforeCreatingDestination()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            await store.CreateBackupAsync(fixture.Backup, token);
        }
        (Guid? Incarnation, byte[]? Key)[] invalid =
        [
            (Guid.Empty, null), (null, []), (null, new byte[31]), (null, new byte[33])
        ];
        foreach (var entry in invalid)
        {
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => ZoneTreeStore.RestoreAsync(
                fixture.Backup, fixture.Restored, UnitExecutionOptions.StorageExecution(), entry.Incarnation, entry.Key, token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(Directory.Exists(fixture.Restored)).IsFalse();
        }
        await ZoneTreeStore.RestoreAsync(fixture.Backup, fixture.Restored, UnitExecutionOptions.StorageExecution(),
            cancellationToken: token);
        using var reopened = ZoneTreeBackupFixture.Open(fixture.Restored);
        await Assert.That(ZoneTreeBackupFixture.Read(reopened, ZoneTreeBackupFixture.BeforeNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 1);
    }

    [Test]
    public async Task CopyRejectsOverlapMissingSourceCancellationAndOccupiedDestinationBeforeEffects()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            await store.CreateBackupAsync(fixture.Backup, token);
        }
        var preserved = await ZoneTreeBackupFixture.CaptureAsync(fixture.Backup, token);
        foreach (var destination in new[] { fixture.Backup, Path.Combine(fixture.Backup, "child"), Path.GetDirectoryName(fixture.Backup)! })
        {
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Backup, destination, token));
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await ZoneTreeBackupFixture.SameFilesAsync(preserved, fixture.Backup, token);
        }
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
            ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Entry("missing"), fixture.Copy, token));
        await Assert.That(Directory.Exists(fixture.Copy)).IsFalse();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Backup, fixture.Copy, cancelled.Token));
        await Assert.That(Directory.Exists(fixture.Copy)).IsFalse();
        Directory.CreateDirectory(fixture.Copy);
        var retainedFile = Path.Combine(fixture.Copy, "retained.txt");
        await File.WriteAllTextAsync(retainedFile, "keep", token);
        var occupied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Backup, fixture.Copy, token));
        await Assert.That(occupied!.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(await File.ReadAllTextAsync(retainedFile, token)).IsEqualTo("keep");
        File.Delete(retainedFile);
        await ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Backup, fixture.Copy, token);
        await ZoneTreeBackupFixture.SameFilesAsync(preserved, fixture.Copy, token);
    }
}
