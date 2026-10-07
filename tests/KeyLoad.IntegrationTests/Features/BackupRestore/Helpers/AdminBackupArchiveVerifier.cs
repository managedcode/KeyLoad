using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.BackupRestore.Helpers;

/// <summary>Finds and restores an administrator backup through current native storage ownership.</summary>
internal static class AdminBackupArchiveVerifier
{
    private const string BackupDirectoryName = "backups";
    private const string BackupManifestName = "backup.json";
    private const string BackupCategory = "backup";
    private const string RestoreDirectoryPrefix = "keyload-sdk-backup-restore-";
    private const string RelativePathSeparator = "/";
    private const int SingleMatchingManifestCount = 1;
    private const string OccupiedRestoreDirectory = "The unique test restore destination is already occupied.";
    private const string MissingRestoredRecord = "The restored archive did not contain the committed document.";
    private const string ArchiveDirectoryMissing = "The observed backup archive directory is absent.";
    private const string ObservedArchiveFileMissing = "An observed backup file is absent from its physical owner.";
    private const string ArchivePathOutsideFixture = "An observed backup path resolved outside its fixture-owned root.";
    private const string MissingVoterIdentity = "The dashboard did not report the executing physical voter.";
    private const string VoterDirectoryOutsideFixture = "The actual voter name did not resolve to one fixture-owned node directory.";
    private const int EmptyInventoryCount = 0;
    private const string DataMountTarget = "/data";
    private static readonly string[] Nodes =
        [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task VerifyRestoreAsync(ClusterFixture fixture, BackupReceipt receipt,
        EntityRef reference, string expectedJson, long expectedRevision, long seedPosition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(reference);
        var archiveDirectory = await LocateArchiveAsync(fixture, receipt, cancellationToken).ConfigureAwait(false);
        var policy = IntegrationExecutionOptions.StorageExecution();
        var (originalIdentity, verifiedPosition) = ZoneTreeStore.VerifyBackup(archiveDirectory, policy);
        var originalManifestDigest = await AdminBackupArchiveIntegrity.CaptureManifestDigestAsync(
            archiveDirectory, policy.Value, cancellationToken).ConfigureAwait(false);
        await Assert.That(verifiedPosition).IsEqualTo(receipt.Position);
        await Assert.That(verifiedPosition).IsGreaterThanOrEqualTo(seedPosition);

        var restoreDirectory = Path.GetFullPath(Path.Combine(fixture.Root, string.Concat(RestoreDirectoryPrefix,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat))));
        if (Directory.Exists(restoreDirectory) || File.Exists(restoreDirectory))
        { throw new IOException(OccupiedRestoreDirectory); }

        var failures = new List<Exception>();
        ZoneTreeStore? restoredStore = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var restoredIdentity = ZoneTreeStore.Restore(archiveDirectory, restoreDirectory, policy);
            await Assert.That(restoredIdentity.Incarnation).IsNotEqualTo(originalIdentity.Incarnation);
            await Assert.That(restoredIdentity.DispatchPaused).IsTrue();
            restoredStore = new(new ZoneTreeStoreOptions(restoreDirectory), policy,
                IntegrationExecutionOptions.PointCacheExecution());
            var storedRecord = restoredStore.Read(view => view.GetRecord<DocumentRecord>(
                DocumentStorageKeys.RecordKey(reference)));
            await Assert.That(storedRecord).IsNotNull();
            var record = storedRecord ?? throw new InvalidDataException(MissingRestoredRecord);
            await Assert.That(record.Reference).IsEqualTo(reference);
            await Assert.That(record.Revision).IsEqualTo(expectedRevision);
            await Assert.That(record.Json).IsEqualTo(expectedJson);
            await Assert.That(record.Deleted).IsFalse();
            var (verifiedIdentityAfterRestore, verifiedPositionAfterRestore) =
                ZoneTreeStore.VerifyBackup(archiveDirectory, policy);
            var unchangedIdentity = NativeSerialization.Serialize(verifiedIdentityAfterRestore).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(originalIdentity));
            await Assert.That(unchangedIdentity).IsTrue();
            await Assert.That(verifiedPositionAfterRestore).IsEqualTo(receipt.Position);
            var manifestDigestAfterRestore = await AdminBackupArchiveIntegrity.CaptureManifestDigestAsync(
                archiveDirectory, policy.Value, cancellationToken).ConfigureAwait(false);
            await Assert.That(manifestDigestAfterRestore.AsSpan().SequenceEqual(originalManifestDigest)).IsTrue();
        }, failures).ConfigureAwait(false);

        if (restoredStore is not null)
        { ServerFailureObserver.Observe(restoredStore.Dispose, failures); }
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
        var expectedManifest = string.Concat(archivePrefix, BackupManifestName);
        var archiveDirectories = new List<string>();
        foreach (var node in Nodes)
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var snapshot = await McpCallerAssertions.SdkSuccessAsync(
                await sdk.DashboardAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(snapshot.Storage.Complete).IsTrue();
            await Assert.That(snapshot.Storage.Files.Length).IsEqualTo(snapshot.Storage.ObservedFiles);
            var voter = snapshot.LocalVoter
                ?? throw new InvalidOperationException(MissingVoterIdentity);
            await Assert.That(snapshot.Voters.Contains(voter, StringComparer.Ordinal)).IsTrue();
            var manifests = snapshot.Storage.Files.Where(file => file.Category == BackupCategory
                && string.Equals(file.Path, expectedManifest, StringComparison.Ordinal)).ToArray();
            if (manifests.Length == EmptyInventoryCount)
            { continue; }
            await Assert.That(manifests.Length).IsEqualTo(SingleMatchingManifestCount);
            var manifest = manifests.Single();
            await Assert.That(manifest.Bytes).IsGreaterThan(EmptyInventoryCount);
            var voterDirectory = ResolveVoterDirectory(fixture, voter);
            var archiveDirectory = ResolveArchiveDirectory(fixture.Root, voterDirectory, receipt.Id);
            await VerifyRetainedArchiveFilesAsync(snapshot.Storage.Files, archivePrefix, fixture.Root,
                voterDirectory, cancellationToken).ConfigureAwait(false);
            var manifestPath = Path.Combine(archiveDirectory, BackupManifestName);
            var manifestFile = new FileInfo(manifestPath);
            await Assert.That(manifestFile.Exists).IsTrue();
            await Assert.That(manifestFile.Length).IsEqualTo(manifest.Bytes);
            archiveDirectories.Add(archiveDirectory);
        }
        await Assert.That(archiveDirectories.Count).IsEqualTo(1);
        return archiveDirectories.Single();
    }

    private static async Task VerifyRetainedArchiveFilesAsync(ImmutableArray<AdminFileInfo> files,
        string archivePrefix, string fixtureRoot, string voterDirectory, CancellationToken cancellationToken)
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
            var path = ResolveFixtureOwnedPath(fixtureRoot, Path.Combine(voterDirectory, relativePath));
            var file = new FileInfo(path);
            if (!file.Exists)
            { throw new InvalidDataException(ObservedArchiveFileMissing); }
            await Assert.That(file.Length).IsEqualTo(observed.Bytes);
        }
    }

    private static string ResolveArchiveDirectory(string fixtureRoot, string voterDirectory, string receiptId)
    {
        var path = Path.Combine(voterDirectory, BackupDirectoryName, receiptId);
        var directory = ResolveFixtureOwnedPath(fixtureRoot, path);
        if (!Directory.Exists(directory))
        { throw new DirectoryNotFoundException(ArchiveDirectoryMissing); }
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
        { throw new InvalidDataException(ArchivePathOutsideFixture); }
        return fullPath;
    }

    private static string ResolveVoterDirectory(ClusterFixture fixture, string voter)
    {
        if (!Uri.TryCreate(voter, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttp
            || !Nodes.Contains(origin.Host, StringComparer.Ordinal) || origin.UserInfo.Length != EmptyInventoryCount
            || origin.GetLeftPart(UriPartial.Authority) != voter)
        { throw new InvalidDataException(VoterDirectoryOutsideFixture); }
        var resource = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Single(item => item.Name == origin.Host);
        var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
            .Single(item => item.Name == McpCallerProtocol.HttpEndpoint);
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
            .Single(item => item.Target == DataMountTarget);
        if (origin.Port != endpoint.TargetPort || mount.Type != ContainerMountType.BindMount || mount.IsReadOnly
            || mount.Source is not { } source)
        { throw new InvalidDataException(VoterDirectoryOutsideFixture); }
        return ResolveFixtureOwnedPath(fixture.Root, source);
    }

    private static void DeleteOwnedRestoreDirectory(string directory)
    {
        if (Directory.Exists(directory))
        { Directory.Delete(directory, recursive: true); }
    }
}
