using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataLinkRestoreContinuation
{
    private const string ChangedOwnedLink = "The original fixture-owned identity link changed before repair.";
    private const int NoAttributes = 0;

    internal static async Task RequireAsync(MetadataBackupFixture fixture, string destination,
        CancellationToken cancellationToken)
    {
        var source = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken).ConfigureAwait(false);
        var backup = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName);
        UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);
        var linkCreated = false;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            File.Delete(path);
            File.CreateSymbolicLink(path, MetadataTestContract.JournalFileName);
            linkCreated = true;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(
                fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileLinkDetail);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, cancellationToken).ConfigureAwait(false);
            foreach (var name in new[] { MetadataTestContract.ManifestFileName, MetadataTestContract.JournalFileName })
            {
                await Assert.That((await File.ReadAllBytesAsync(Path.Combine(fixture.BackupDirectory, name), cancellationToken)
                    .ConfigureAwait(false)).AsSpan().SequenceEqual(backup[name])).IsTrue();
            }
        }, failures).ConfigureAwait(false);
        if (linkCreated)
        {
            await ServerFailureObserver.ObserveAsync(() => RepairAsync(path,
                backup[MetadataTestContract.IdentityFileName], mode, cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, cancellationToken).ConfigureAwait(false);
        await MetadataRestoreAssertions.AssertRestored(fixture, destination).ConfigureAwait(false);
        await RequireColdAsync(fixture, destination).ConfigureAwait(false);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, cancellationToken).ConfigureAwait(false);
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RepairAsync(string path, byte[] original, UnixFileMode? mode,
        CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == NoAttributes
            || !string.Equals(new FileInfo(path).LinkTarget, MetadataTestContract.JournalFileName, StringComparison.Ordinal))
        { throw new InvalidOperationException(ChangedOwnedLink); }
        File.Delete(path);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            UnitExecutionOptions.StorageExecution().Value.StreamBufferBytes, FileOptions.Asynchronous);
        await file.WriteAsync(original, cancellationToken).ConfigureAwait(false);
        FlushDurably(file);
        if (!OperatingSystem.IsWindows() && mode is { } originalMode)
        { File.SetUnixFileMode(path, originalMode); }
    }

    private static void FlushDurably(FileStream file) => file.Flush(true);

    private static async Task RequireColdAsync(MetadataBackupFixture fixture, string destination)
    {
        StoreIdentity original;
        long originalPosition;
        using (var first = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            original = first.Identity;
            originalPosition = first.Position;
            await Assert.That(original.Incarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
            await Assert.That(original.DispatchPaused).IsTrue();
            await RequireLiteralAsync(first).ConfigureAwait(false);
        }
        using var cold = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(cold.Identity.FormatVersion).IsEqualTo(original.FormatVersion);
        await Assert.That(cold.Identity.KeyCodecVersion).IsEqualTo(original.KeyCodecVersion);
        await Assert.That(cold.Identity.NodeId).IsEqualTo(original.NodeId);
        await Assert.That(cold.Identity.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(cold.Identity.SigningKey.Span.SequenceEqual(original.SigningKey.Span)).IsTrue();
        await Assert.That(cold.Identity.DispatchPaused).IsTrue();
        await Assert.That(cold.Identity.Durability).IsEqualTo(original.Durability);
        await Assert.That(cold.Identity.ReadGeneration).IsEqualTo(original.ReadGeneration);
        await Assert.That(cold.Identity.MinimumReaderContract).IsEqualTo(original.MinimumReaderContract);
        await Assert.That(cold.Position).IsEqualTo(originalPosition);
        await RequireLiteralAsync(cold).ConfigureAwait(false);
    }

    private static async Task RequireLiteralAsync(ZoneTreeStore store)
        => await Assert.That(store.Read(view => NativeSerialization.Deserialize<string>(
            view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!))).IsEqualTo(MetadataBackupFixture.ExpectedValue);
}
