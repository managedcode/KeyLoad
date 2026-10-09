using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CliBackupRestoreUnsafeDestinationTrial
{
    private const string BackupCommand = "backup";
    private const string RestoreCommand = "restore";
    private const string LinkedChild = "restored-child";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const string ConflictCode = "Conflict";
    private const string ExpectedPathDetail = "The native catalog backup destination is not clean.";
    private const string ChangedLink = "The original fixture-owned destination link changed before cleanup.";
    private const string RestoreStagingPrefix = ".keyload-restore-";
    private const int FailedExit = 1;
    private const int NoAttributes = 0;

    internal static async Task RunAsync(IOptions<TestExecutionOptions> options, CliBackupRestoreFixture fixture,
        bool ancestor, CancellationToken cancellationToken)
    {
        var backupResult = await CliBackupRestoreProcess.RunAsync(options,
            [BackupCommand, fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken).ConfigureAwait(false);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(backupResult).ConfigureAwait(false);
        var source = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken).ConfigureAwait(false);
        var backup = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(fixture.ExistingDestination);
        var link = ancestor ? fixture.UnpackedDirectory : fixture.RestoredDirectory;
        var destination = ancestor ? Path.Combine(link, LinkedChild) : link;
        Directory.CreateSymbolicLink(link, fixture.ExistingDestination);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var rejected = await CliBackupRestoreProcess.RunAsync(options,
                [RestoreCommand, fixture.BackupDirectory, destination], cancellationToken).ConfigureAwait(false);
            await RequireRefusalAsync(rejected).ConfigureAwait(false);
            await Assert.That(new DirectoryInfo(link).LinkTarget).IsEqualTo(fixture.ExistingDestination);
            await RequireUnchangedAsync(fixture, source, backup, cancellationToken).ConfigureAwait(false);
            await Assert.That(Directory.EnumerateFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path).StartsWith(RestoreStagingPrefix, StringComparison.Ordinal))).IsFalse();
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => RemoveOwnedLink(link, fixture.ExistingDestination), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        if (ancestor) { Directory.CreateDirectory(link); }
        var healthy = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.BackupDirectory, destination], cancellationToken).ConfigureAwait(false);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(healthy).ConfigureAwait(false);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, healthy, destination).ConfigureAwait(false);
        await RequireColdAsync(fixture, destination).ConfigureAwait(false);
        await RequireUnchangedAsync(fixture, source, backup, cancellationToken).ConfigureAwait(false);
        await fixture.AssertSeedRemainsAsync().ConfigureAwait(false);
    }

    private static async Task RequireRefusalAsync(CliBackupRestoreProcessResult result)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(FailedExit);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(ConflictCode);
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(ExpectedPathDetail);
    }

    private static void RemoveOwnedLink(string link, string target)
    {
        if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == NoAttributes
            || !string.Equals(new DirectoryInfo(link).LinkTarget, target, StringComparison.Ordinal))
        { throw new InvalidOperationException(ChangedLink); }
        Directory.Delete(link, recursive: false);
    }

    private static async Task RequireUnchangedAsync(CliBackupRestoreFixture fixture,
        IReadOnlyDictionary<string, byte[]> source, IReadOnlyDictionary<string, byte[]> backup,
        CancellationToken cancellationToken)
    {
        await Assert.That(Directory.EnumerateFileSystemEntries(fixture.ExistingDestination).Any()).IsFalse();
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, cancellationToken).ConfigureAwait(false);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireColdAsync(CliBackupRestoreFixture fixture, string destination)
    {
        StoreIdentity original;
        long originalPosition;
        using (var first = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            original = first.Identity;
            originalPosition = first.Position;
            await Assert.That(original.Incarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
            await Assert.That(original.SigningKey.Span.SequenceEqual(fixture.OriginalIdentity.SigningKey.Span)).IsFalse();
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
            view.ReadOwnedValue(CliBackupRestoreFixture.StoredKeyBytes)!))).IsEqualTo(CliBackupRestoreFixture.StoredValue);
}
