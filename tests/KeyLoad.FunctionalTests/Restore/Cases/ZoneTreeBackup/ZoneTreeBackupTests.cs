using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

/// <summary>Qualifies the ZoneTree live backup and restore wrapper of one store.</summary>
internal sealed class ZoneTreeBackupTests
{
    private const string PreservedFileName = "preserved.txt";
    private const string PreservedFileContents = "keep";

    [Test]
    public async Task BackupHoldsCommitsThroughItsPositionAndRestoreOpensPausedUnderNewIdentity()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        StoreIdentity source;
        long position;
        long finalSourcePosition;
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            source = store.Identity;
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 2);
            position = await store.CreateBackupAsync(fixture.Backup, token);
            await Assert.That(position).IsEqualTo(store.Position);
            var write = ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.AfterNamespace, 1);
            await Assert.That(write).IsGreaterThan(position);
            finalSourcePosition = await NativeStoreAssertions.AdvancedAsync(store, position);
            await Assert.That(write).IsLessThanOrEqualTo(finalSourcePosition);
        }
        var backup = await ZoneTreeBackupFixture.CaptureAsync(fixture.Backup, token);
        await ZoneTreeBackupAssertions.LayoutAsync(fixture, backup, token);
        var restored = await ZoneTreeStore.RestoreAsync(fixture.Backup, fixture.Restored,
            UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        await ZoneTreeBackupAssertions.NewIdentityAsync(source, restored);
        var restoredPosition = await ZoneTreeBackupAssertions.RestoredCutAsync(fixture.Restored, restored);
        await ZoneTreeBackupAssertions.HealthyFollowUpAsync(fixture.Restored, restored, restoredPosition);
        await ZoneTreeBackupFixture.SameFilesAsync(backup, fixture.Backup, token);
        using var original = ZoneTreeBackupFixture.Open(fixture.Source);
        finalSourcePosition = await NativeStoreAssertions.ReopenedAsync(original, finalSourcePosition);
        await StoreFileFixture.AssertPreservedIdentity(source, original.Identity);
        await Assert.That(original.Position).IsEqualTo(finalSourcePosition);
        await Assert.That(ZoneTreeBackupFixture.Read(original, ZoneTreeBackupFixture.AfterNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.AfterNamespace + 1);
    }

    [Test]
    public async Task RestoreAppliesTheRequestedIncarnationAndSigningKey()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            await store.CreateBackupAsync(fixture.Backup, token);
        }
        var incarnation = Guid.NewGuid();
        var signingKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var restored = await ZoneTreeStore.RestoreAsync(fixture.Backup, fixture.Restored,
            UnitExecutionOptions.StorageExecution(), incarnation, signingKey, token);
        await Assert.That(restored.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restored.SigningKey.ToArray()).IsEquivalentTo(signingKey, CollectionOrdering.Matching);
        using var reopened = ZoneTreeBackupFixture.Open(fixture.Restored);
        await StoreFileFixture.AssertPreservedIdentity(restored, reopened.Identity);
        await Assert.That(ZoneTreeBackupFixture.Read(reopened, ZoneTreeBackupFixture.BeforeNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 1);
    }

    [Test]
    public async Task NonEmptyDestinationsAreRejectedWithConflictAndLeftUnchanged()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        var occupied = fixture.Entry("occupied");
        Directory.CreateDirectory(occupied);
        await File.WriteAllTextAsync(Path.Combine(occupied, PreservedFileName), PreservedFileContents, token);
        var preserved = await ZoneTreeBackupFixture.CaptureAsync(occupied, token);
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            var backupFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => store.CreateBackupAsync(occupied, token));
            await Assert.That((backupFailure ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.Conflict);
            await ZoneTreeBackupFixture.SameFilesAsync(preserved, occupied, token);
            Directory.CreateDirectory(fixture.Backup);
            await Assert.That(await store.CreateBackupAsync(fixture.Backup, token)).IsEqualTo(store.Position);
        }
        var restoreFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => ZoneTreeStore.RestoreAsync(
            fixture.Backup, occupied, UnitExecutionOptions.StorageExecution(), cancellationToken: token));
        await Assert.That((restoreFailure ?? throw new InvalidOperationException()).Code).IsEqualTo(ErrorCode.Conflict);
        await ZoneTreeBackupFixture.SameFilesAsync(preserved, occupied, token);
        Directory.CreateDirectory(fixture.Restored);
        var restored = await ZoneTreeStore.RestoreAsync(fixture.Backup, fixture.Restored,
            UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        using var reopened = ZoneTreeBackupFixture.Open(fixture.Restored);
        await StoreFileFixture.AssertPreservedIdentity(restored, reopened.Identity);
        await Assert.That(ZoneTreeBackupFixture.Read(reopened, ZoneTreeBackupFixture.BeforeNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 1);
    }

    [Test]
    public async Task CancelledBackupAndRestoreCreateNoDestination()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.CreateBackupAsync(fixture.Backup, cancelled.Token));
            await Assert.That(Directory.Exists(fixture.Backup)).IsFalse();
            await store.CreateBackupAsync(fixture.Backup, token);
        }
        await Assert.ThrowsAsync<OperationCanceledException>(() => ZoneTreeStore.RestoreAsync(fixture.Backup,
            fixture.Restored, UnitExecutionOptions.StorageExecution(), cancellationToken: cancelled.Token));
        await Assert.That(Directory.Exists(fixture.Restored)).IsFalse();
        await ZoneTreeStore.RestoreAsync(fixture.Backup, fixture.Restored, UnitExecutionOptions.StorageExecution(),
            cancellationToken: token);
        using var reopened = ZoneTreeBackupFixture.Open(fixture.Restored);
        await Assert.That(ZoneTreeBackupFixture.Read(reopened, ZoneTreeBackupFixture.BeforeNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 1);
    }

    [Test]
    public async Task CopyToFileStorageKeepsEveryBackupFileAtItsRelativePathAndTheCopyRestores()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new ZoneTreeBackupFixture();
        using (var store = ZoneTreeBackupFixture.Open(fixture.Source))
        {
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 1);
            ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.BeforeNamespace, 2);
            await store.CreateBackupAsync(fixture.Backup, token);
        }
        var backup = await ZoneTreeBackupFixture.CaptureAsync(fixture.Backup, token);
        await ZoneTreeBackupCopy.CopyToFileStorageAsync(fixture.Backup, fixture.Copy, token);
        await ZoneTreeBackupFixture.SameFilesAsync(backup, fixture.Copy, token);
        await ZoneTreeBackupFixture.SameFilesAsync(backup, fixture.Backup, token);
        await ZoneTreeStore.RestoreAsync(fixture.Copy, fixture.Restored, UnitExecutionOptions.StorageExecution(),
            cancellationToken: token);
        using var reopened = ZoneTreeBackupFixture.Open(fixture.Restored);
        await Assert.That(reopened.Position).IsGreaterThan(0);
        await Assert.That(ZoneTreeBackupFixture.Read(reopened, ZoneTreeBackupFixture.BeforeNamespace, 2))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 2);
    }
}
