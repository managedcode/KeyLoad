using System.Text.Json;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsRpc1SourceTests
{
    private const string Revision = "377886f35928866f083806062b446056d64539e3";
    private const string TreeSha = "937b2c0d576ef29afb33e943ddd452722ee6a16b";
    private const string InventorySha = "70e92d99a96ed508ab4a1e617a5884d95cf3bfc97e99f3e131748a99cd6b640f";
    private const string ArchiveSha = "f4a36d2febcae6e35e857c735cbebe55d41ecd9dc2652b22a67b5f541039807f";
    private const string ArchiveName = "rpc1-epoch6-server-source.tar";
    private const string InventoryName = "rpc1-epoch6-server-source-inventory.json";
    private const string SourceModule = "rpc1-server-source.mjs";
    private const string ArchiveModule = "native5-server-archive.mjs";
    private const string RevisionKey = "Revision";
    private const string TreeKey = "TreeSha";
    private const string InventoryKey = "InventorySha256";
    private const string InventoryCountKey = "InventoryCount";
    private const string ExpandedBytesKey = "ExpandedBytes";
    private const string ArchiveKey = "ArchiveSha256";
    private const string ArchiveBytesKey = "ArchiveBytes";
    private const string ArchiveNameKey = "ArchiveName";
    private const string InventoryNameKey = "InventoryName";
    private const string DataEpochKey = "DataEpoch";
    private const string RequestInterfaceVersionKey = "RequestInterfaceVersion";
    private const string PeerEnvelopeVersionKey = "PeerEnvelopeVersion";
    private const string RetainedVerifiedKey = "RetainedArchiveVerified";
    private const string DefaultRejectedKey = "Native5DefaultRejected";
    private const string InvalidRevisionRejectedKey = "InvalidRevisionRejected";
    private const string ArtifactsPreservedKey = "ArtifactsPreserved";
    private const string SourceUnchangedKey = "SourceUnchanged";
    private const string CleanupCompleteKey = "CleanupComplete";
    private const string Probe = """
        import { createHash } from 'node:crypto';
        import { createReadStream } from 'node:fs';
        import { lstat, mkdir, mkdtemp, readFile, rm } from 'node:fs/promises';
        import os from 'node:os';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const sourceModule = await import(pathToFileURL(process.argv[1]).href);
        const archiveModule = await import(pathToFileURL(process.argv[2]).href);
        const root = await mkdtemp(path.join(os.tmpdir(), 'keyload-rpc1-source-'));
        const evidence = path.join(root, 'evidence');
        await mkdir(evidence, { mode: 0o700 });
        let source;
        let primary;
        let summary;
        try {
          source = await sourceModule.createRpc1ServerSource({ runnerTemp: root, runId: String(process.pid),
            runAttempt: '1', workspace: process.cwd(), evidenceDirectory: evidence });
          const expected = { revision: '377886f35928866f083806062b446056d64539e3',
            treeSha: '937b2c0d576ef29afb33e943ddd452722ee6a16b',
            inventorySha: '70e92d99a96ed508ab4a1e617a5884d95cf3bfc97e99f3e131748a99cd6b640f',
            files: 3016, expandedBytes: 25738907, archiveBytes: 28180480,
            archiveSha: 'f4a36d2febcae6e35e857c735cbebe55d41ecd9dc2652b22a67b5f541039807f',
            archiveName: 'rpc1-epoch6-server-source.tar', inventoryName: 'rpc1-epoch6-server-source-inventory.json' };
          const identity = sourceModule.rpc1ServerSource;
          const beforeExport = await sourceModule.verifyRpc1ServerExport(source);
          const archiveBefore = await fileSnapshot(source.archivePath);
          const inventoryBefore = await fileSnapshot(source.inventoryPath);
          const inventoryBytes = await readFile(source.inventoryPath);
          const inventoryDocument = JSON.parse(inventoryBytes.toString('utf8'));
          const positive = await archiveModule.verifyRetainedArchive(evidence, identity.archiveName,
            source.inventory.files, expected.archiveBytes, expected.archiveSha, expected.revision);
          const defaultRejected = await rejectsExpected(() => archiveModule.verifyRetainedArchive(evidence,
            identity.archiveName, source.inventory.files, expected.archiveBytes, expected.archiveSha));
          const afterDefault = await sameArtifacts(source, archiveBefore, inventoryBefore);
          const invalidRevisionRejected = await rejectsExpected(() => archiveModule.verifyRetainedArchive(evidence,
            identity.archiveName, source.inventory.files, expected.archiveBytes, expected.archiveSha, 'not-a-revision'));
          const afterInvalid = await sameArtifacts(source, archiveBefore, inventoryBefore);
          const afterExport = await sourceModule.verifyRpc1ServerExport(source);
          const sourceUnchanged = sameInventory(beforeExport, afterExport);
          const artifactsPreserved = afterDefault && afterInvalid
            && await sameArtifacts(source, archiveBefore, inventoryBefore)
            && sha256(inventoryBytes) === inventoryBefore.sha256;
          summary = {
            Revision: identity.revision, TreeSha: identity.treeSha, InventorySha256: afterExport.sourceInventorySha256,
            InventoryCount: inventoryDocument.files.length, ExpandedBytes: afterExport.expandedBytes,
            ArchiveSha256: positive.archiveSha256, ArchiveBytes: positive.archiveBytes,
            ArchiveName: identity.archiveName, InventoryName: identity.inventoryName,
            DataEpoch: identity.dataEpoch, RequestInterfaceVersion: identity.requestInterfaceVersion,
            PeerEnvelopeVersion: identity.peerEnvelopeVersion,
            RetainedArchiveVerified: positive.files.length === expected.files && positive.expandedBytes === expected.expandedBytes,
            Native5DefaultRejected: defaultRejected, InvalidRevisionRejected: invalidRevisionRejected,
            ArtifactsPreserved: artifactsPreserved, SourceUnchanged: sourceUnchanged, CleanupComplete: false,
          };
          if (identity.revision !== expected.revision || identity.treeSha !== expected.treeSha
            || source.archiveBytes !== expected.archiveBytes || source.archiveSha256 !== expected.archiveSha
            || source.inventory.fileCount !== expected.files || source.inventory.expandedBytes !== expected.expandedBytes
            || source.inventory.sourceInventorySha256 !== expected.inventorySha
            || identity.archiveName !== expected.archiveName || identity.inventoryName !== expected.inventoryName
            || source.archivePath !== path.join(evidence, expected.archiveName)
            || source.inventoryPath !== path.join(evidence, expected.inventoryName)
            || afterExport.fileCount !== expected.files || afterExport.expandedBytes !== expected.expandedBytes
            || afterExport.sourceInventorySha256 !== expected.inventorySha || inventoryDocument.schemaVersion !== 1
            || inventoryDocument.files.length !== expected.files || positive.archiveBytes !== expected.archiveBytes
            || positive.archiveSha256 !== expected.archiveSha || !defaultRejected || !invalidRevisionRejected
            || !artifactsPreserved || !sourceUnchanged) throw new Error('The RPC1 immutable source contract failed.');
        } catch (error) {
          primary = error;
        }
        const cleanupFailures = [];
        if (primary?.preserveOwnedPaths !== true) {
          if (source) {
            try { await sourceModule.cleanupRpc1ServerSource(source); } catch (error) { cleanupFailures.push(error); }
          }
          try { await rm(root, { recursive: true, force: false }); } catch (error) { cleanupFailures.push(error); }
        }
        if (primary && cleanupFailures.length > 0) throw new AggregateError([primary, ...cleanupFailures], 'RPC1 source probe failed and cleanup was incomplete.');
        if (primary) throw primary;
        if (cleanupFailures.length > 0) throw new AggregateError(cleanupFailures, 'RPC1 source probe cleanup failed.');
        summary.CleanupComplete = !(await exists(root));
        process.stdout.write(JSON.stringify(summary));

        async function fileSnapshot(file) {
          const info = await lstat(file);
          if (!info.isFile() || info.isSymbolicLink() || (info.mode & 0o077) !== 0 || info.uid !== process.getuid()) {
            throw new Error('Expected a private regular retained source artifact.');
          }
          return { dev: String(info.dev), ino: String(info.ino), size: info.size, mode: info.mode, sha256: await fileSha(file) };
        }
        async function sameArtifacts(value, archive, inventory) {
          return sameSnapshot(await fileSnapshot(value.archivePath), archive)
            && sameSnapshot(await fileSnapshot(value.inventoryPath), inventory);
        }
        function sameSnapshot(left, right) {
          return left.dev === right.dev && left.ino === right.ino && left.size === right.size
            && left.mode === right.mode && left.sha256 === right.sha256;
        }
        async function rejectsExpected(action) {
          try { await action(); return false; }
          catch (error) { return error?.message === 'The immutable native5 source export is invalid.'; }
        }
        function sameInventory(left, right) {
          return left.fileCount === right.fileCount && left.expandedBytes === right.expandedBytes
            && left.sourceInventorySha256 === right.sourceInventorySha256 && left.files.length === right.files.length
            && left.files.every((row, index) => row.path === right.files[index].path && row.mode === right.files[index].mode
              && row.bytes === right.files[index].bytes && row.sha256 === right.files[index].sha256);
        }
        async function fileSha(file) {
          const hash = createHash('sha256');
          for await (const chunk of createReadStream(file)) hash.update(chunk);
          return hash.digest('hex');
        }
        async function exists(file) {
          try { await lstat(file); return true; }
          catch (error) { if (error?.code === 'ENOENT') return false; throw error; }
        }
        function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
        """;

    [Test]
    public async Task AcCrs002And006BindRpc1ExportToTheImmutablePriorCommit()
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Probe, SourceModulePath(), ArchiveModulePath()],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        await Assert.That(report.GetProperty(RevisionKey).GetString()).IsEqualTo(Revision);
        await Assert.That(report.GetProperty(TreeKey).GetString()).IsEqualTo(TreeSha);
        await Assert.That(report.GetProperty(InventoryKey).GetString()).IsEqualTo(InventorySha);
        await Assert.That(report.GetProperty(InventoryCountKey).GetInt32()).IsEqualTo(3016);
        await Assert.That(report.GetProperty(ExpandedBytesKey).GetInt64()).IsEqualTo(25738907);
        await Assert.That(report.GetProperty(ArchiveKey).GetString()).IsEqualTo(ArchiveSha);
        await Assert.That(report.GetProperty(ArchiveBytesKey).GetInt64()).IsEqualTo(28180480);
        await Assert.That(report.GetProperty(ArchiveNameKey).GetString()).IsEqualTo(ArchiveName);
        await Assert.That(report.GetProperty(InventoryNameKey).GetString()).IsEqualTo(InventoryName);
        await Assert.That(report.GetProperty(DataEpochKey).GetInt32()).IsEqualTo(6);
        await Assert.That(report.GetProperty(RequestInterfaceVersionKey).GetInt32()).IsEqualTo(1);
        await Assert.That(report.GetProperty(PeerEnvelopeVersionKey).GetInt32()).IsEqualTo(2);
        await Assert.That(report.GetProperty(RetainedVerifiedKey).GetBoolean()).IsTrue();
        await Assert.That(report.GetProperty(DefaultRejectedKey).GetBoolean()).IsTrue();
        await Assert.That(report.GetProperty(InvalidRevisionRejectedKey).GetBoolean()).IsTrue();
        await Assert.That(report.GetProperty(ArtifactsPreservedKey).GetBoolean()).IsTrue();
        await Assert.That(report.GetProperty(SourceUnchangedKey).GetBoolean()).IsTrue();
        await Assert.That(report.GetProperty(CleanupCompleteKey).GetBoolean()).IsTrue();
    }

    private static string SourceModulePath()
        => Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "scripts", "Features", "ClusterRouting", SourceModule);

    private static string ArchiveModulePath()
        => Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), "scripts", "Features", "StorageRecovery", ArchiveModule);
}
