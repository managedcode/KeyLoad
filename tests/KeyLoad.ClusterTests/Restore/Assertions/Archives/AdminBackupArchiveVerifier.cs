using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ClusterTests.BackupRestore.Helpers;

/// <summary>Finds and restores an administrator backup through current execution storage ownership.</summary>
internal static class AdminBackupArchiveVerifier
{
    private const string BackupDirectoryName = "backups";
    private const string BackupIdentityName = "identity.json";
    private const string BackupCategory = "backup";
    private const string RestoreDirectoryPrefix = "keyload-sdk-backup-restore-";
    private const string RelativePathSeparator = "/";
    private const string OccupiedRestoreDirectory = "The unique test restore destination is already occupied.";
    private const string MissingRestoredRecord = "The restored archive did not contain the committed document.";
    private const string ArchiveDirectoryMissing = "The observed backup archive directory is absent.";
    private const string ObservedArchiveFileMissing = "An observed backup file is absent from its physical owner.";
    private const string ArchivePathOutsideFixture = "An observed backup path resolved outside its fixture-owned root.";
    private const string MissingSiloIdentity = "The dashboard did not report the executing physical silo.";
    private const string SiloDirectoryOutsideFixture = "The actual silo name did not resolve to one fixture-owned node directory.";
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private static readonly string[] Nodes =
        [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task VerifyRestoreAsync(ClusterFixture fixture, BackupReceipt receipt,
        EntityRef reference, string expectedJson, long expectedRevision, long minimumLocalPosition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(reference);
        var archiveDirectory = await LocateArchiveAsync(fixture, receipt, cancellationToken).ConfigureAwait(false);
        var policy = IntegrationExecutionOptions.StorageExecution();
        var originalIdentity = ZoneTreeIdentityFile.Read(Path.Combine(archiveDirectory, BackupIdentityName),
            policy.Value.MaximumIdentityFileBytes, policy.Value.StreamBufferBytes);
        var originalArchiveDigest = await AdminBackupArchiveIntegrity.CaptureArchiveDigestAsync(
            archiveDirectory, policy.Value, cancellationToken).ConfigureAwait(false);
        await Assert.That(receipt.Position).IsGreaterThanOrEqualTo(minimumLocalPosition);

        var restoreDirectory = Path.GetFullPath(Path.Combine(fixture.Root, string.Concat(RestoreDirectoryPrefix,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat))));
        if (Directory.Exists(restoreDirectory) || File.Exists(restoreDirectory))
        {
            throw new IOException(OccupiedRestoreDirectory);
        }

        var failures = new List<Exception>();
        ZoneTreeStore? restoredStore = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var restoredIdentity = await ZoneTreeStore.RestoreAsync(archiveDirectory, restoreDirectory, policy,
                cancellationToken: cancellationToken);
            await Assert.That(restoredIdentity.NodeId).IsNotEqualTo(originalIdentity.NodeId);
            await Assert.That(restoredIdentity.Incarnation).IsNotEqualTo(originalIdentity.Incarnation);
            await Assert.That(restoredIdentity.DispatchPaused).IsTrue();
            restoredStore = new(new ZoneTreeStoreOptions(restoreDirectory), policy);
            await Assert.That(restoredStore.Position).IsGreaterThan(receipt.Position);
            var storedRecord = restoredStore.Read(view => view.GetRecord<DocumentRecord>(
                DocumentStorageKeys.RecordKey(reference)));
            await Assert.That(storedRecord).IsNotNull();
            var record = storedRecord ?? throw new InvalidDataException(MissingRestoredRecord);
            await Assert.That(record.Reference).IsEqualTo(reference);
            await Assert.That(record.Revision).IsEqualTo(expectedRevision);
            await Assert.That(record.Json).IsEqualTo(expectedJson);
            await Assert.That(record.Deleted).IsFalse();
            var archiveDigestAfterRestore = await AdminBackupArchiveIntegrity.CaptureArchiveDigestAsync(
                archiveDirectory, policy.Value, cancellationToken).ConfigureAwait(false);
            await Assert.That(archiveDigestAfterRestore.AsSpan().SequenceEqual(originalArchiveDigest)).IsTrue();
        }, failures).ConfigureAwait(false);

        if (restoredStore is not null)
        {
            ServerFailureObserver.Observe(restoredStore.Dispose, failures);
        }
        ServerFailureObserver.Observe(() => DeleteOwnedRestoreDirectory(restoreDirectory), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<string> LocateArchiveAsync(ClusterFixture fixture, BackupReceipt receipt,
        CancellationToken cancellationToken)
    {
        var validId = Guid.TryParseExact(receipt.Id, McpCallerProtocol.GuidFormat, out var id) && id != Guid.Empty;
        await Assert.That(validId).IsTrue();
        await Assert.That(id.ToString(McpCallerProtocol.GuidFormat)).IsEqualTo(receipt.Id);
        await Assert.That(receipt.Position).IsGreaterThanOrEqualTo(0);
        var archivePrefix = string.Concat(BackupDirectoryName, RelativePathSeparator, receipt.Id, RelativePathSeparator);
        var expectedIdentity = string.Concat(archivePrefix, BackupIdentityName);
        var archiveDirectories = new List<string>();
        foreach (var node in Nodes)
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var snapshot = await McpCallerAssertions.SdkSuccessAsync(
                await sdk.DashboardAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(snapshot.Storage.Complete).IsTrue();
            await Assert.That(snapshot.Storage.Files.Length).IsEqualTo(snapshot.Storage.ObservedFiles);
            var silo = snapshot.SiloName
                ?? throw new InvalidOperationException(MissingSiloIdentity);
            await Assert.That(snapshot.ActiveSilos.Contains(silo, StringComparer.Ordinal)).IsTrue();
            var identities = snapshot.Storage.Files.Where(file => file.Category == BackupCategory
                && string.Equals(file.Path, expectedIdentity, StringComparison.Ordinal)).ToArray();
            if (identities.Length == 0)
            {
                continue;
            }
            await Assert.That(identities.Length).IsEqualTo(1);
            var identity = identities.Single();
            await Assert.That(identity.Bytes).IsGreaterThan(0);
            var siloDirectory = ResolveSiloDirectory(fixture, silo);
            var archiveDirectory = ResolveArchiveDirectory(fixture.Root, siloDirectory, receipt.Id);
            await VerifyRetainedArchiveFilesAsync(snapshot.Storage.Files, archivePrefix, fixture.Root,
                siloDirectory, cancellationToken).ConfigureAwait(false);
            var identityFile = new FileInfo(Path.Combine(archiveDirectory, BackupIdentityName));
            await Assert.That(identityFile.Exists).IsTrue();
            await Assert.That(identityFile.Length).IsEqualTo(identity.Bytes);
            archiveDirectories.Add(archiveDirectory);
        }
        await Assert.That(archiveDirectories.Count).IsEqualTo(1);
        return archiveDirectories.Single();
    }

    private static async Task VerifyRetainedArchiveFilesAsync(ImmutableArray<AdminFileInfo> files,
        string archivePrefix, string fixtureRoot, string siloDirectory, CancellationToken cancellationToken)
    {
        const int NoObservedArchiveFiles = 0;

        var archiveFiles = files.Where(file => file.Category == BackupCategory
            && file.Path.StartsWith(archivePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(archiveFiles.Length > NoObservedArchiveFiles).IsTrue();
        foreach (var observed in archiveFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = observed.Path.Replace(RelativePathSeparator, Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal);
            var path = ResolveFixtureOwnedPath(fixtureRoot, Path.Combine(siloDirectory, relativePath));
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new InvalidDataException(ObservedArchiveFileMissing);
            }
            await Assert.That(file.Length).IsEqualTo(observed.Bytes);
        }
    }

    private static string ResolveArchiveDirectory(string fixtureRoot, string siloDirectory, string receiptId)
    {
        var path = Path.Combine(siloDirectory, BackupDirectoryName, receiptId);
        var directory = ResolveFixtureOwnedPath(fixtureRoot, path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(ArchiveDirectoryMissing);
        }
        return directory;
    }

    private static string ResolveFixtureOwnedPath(string fixtureRoot, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixtureRoot));
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : string.Concat(root, Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(ArchivePathOutsideFixture);
        }
        return fullPath;
    }

    private static string ResolveSiloDirectory(ClusterFixture fixture, string silo)
    {
        if (!Nodes.Contains(silo, StringComparer.Ordinal))
        {
            throw new InvalidDataException(SiloDirectoryOutsideFixture);
        }
        var resource = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ProjectResource>().Single(item => item.Name == silo);
        if (!fixture.App.ResourceNotifications.TryGetCurrentState(silo, out var current)
            || current.Resource != resource
            || current.Snapshot.EnvironmentVariables.Single(value => value.Name == DataEnvironment).Value is not { } source)
        {
            throw new InvalidDataException(SiloDirectoryOutsideFixture);
        }
        var expected = Path.GetFullPath(Path.Combine(fixture.Root, silo));
        if (!string.Equals(Path.GetFullPath(source), expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(SiloDirectoryOutsideFixture);
        }
        return ResolveFixtureOwnedPath(fixture.Root, source);
    }

    private static void DeleteOwnedRestoreDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
