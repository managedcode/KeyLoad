using System.Text.Json;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;
using KeyLoad.UnitTests.Features.ClusterRouting.Models;
using KeyLoad.UnitTests.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal static class Interface3ServerSourceProbe
{
    private const string Probe = """
        import { createHash } from 'node:crypto';
        import { createReadStream } from 'node:fs';
        import { chmod, lstat, mkdir, mkdtemp, readFile, rm, rmdir } from 'node:fs/promises';
        import os from 'node:os';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const sourceModule = await import(pathToFileURL(process.argv[1]).href);
        const proofModule = await import(pathToFileURL(process.argv[2]).href);
        const expectedRevision = '1e8833c027cf232e35fe012cd3eed41c61a17f89';
        const repositoryWorkspace = process.cwd();
        const root = await mkdtemp(path.join(os.tmpdir(), 'keyload-interface3-source-unit-'));
        const evidenceDirectory = path.join(root, 'evidence');
        await chmod(root, 0o700);
        await mkdir(evidenceDirectory, { mode: 0o700 });
        let source;
        let result;
        let primary;
        try {
          const proofModuleLoaded = typeof proofModule.parseInterface3ServerProof === 'function'
            && typeof proofModule.createInterface3ExpectedProducer === 'function';
          const identity = sourceModule.interface3ServerSource;
          const protocolMetadataValid = identity.dataEpoch === 7
            && identity.requestInterfaceAlias === 'keyload.request.v2'
            && identity.requestInterfaceVersion === 3 && identity.peerEnvelopeVersion === 3;
          if (!proofModuleLoaded) throw new Error('The interface3 proof module exports are incomplete.');
          if (identity.revision !== expectedRevision) throw new Error('The interface3 source pin changed.');
          if (!protocolMetadataValid) throw new Error('The interface3 protocol profile metadata changed.');
          source = await sourceModule.createInterface3ServerSource({ runnerTemp: root,
            runId: String(process.pid), runAttempt: '1', workspace: repositoryWorkspace, evidenceDirectory });
          const sourceMetadataValid = source.repositoryWorkspace === repositoryWorkspace
            && source.workspace === path.join(source.ownedRoot, 'source')
            && source.archivePath === path.join(evidenceDirectory, identity.archiveName)
            && source.inventoryPath === path.join(evidenceDirectory, identity.inventoryName)
            && /^[a-f0-9]{40}$/.test(source.treeSha) && source.gitEntries instanceof Map
            && source.gitEntries.size === source.inventory.fileCount && source.inventory.fileCount > 0
            && source.inventory.expandedBytes > 0 && source.inventory.files.length === source.inventory.fileCount
            && /^[a-f0-9]{64}$/.test(source.inventory.sourceInventorySha256)
            && /^[a-f0-9]{64}$/.test(source.inventoryFileSha256)
            && Number.isSafeInteger(source.archiveBytes) && source.archiveBytes > 0
            && /^[a-f0-9]{64}$/.test(source.archiveSha256);
          if (!sourceMetadataValid) throw new Error('The interface3 source export metadata is incomplete.');
          const repositoryWorkspaceIsActual = source.repositoryWorkspace === repositoryWorkspace
            && await exists(path.join(repositoryWorkspace, '.git'))
            && source.archivePath === path.join(evidenceDirectory, identity.archiveName)
            && source.inventoryPath === path.join(evidenceDirectory, identity.inventoryName);
          const extractedWorkspaceHasNoGit = source.workspace === path.join(source.ownedRoot, 'source')
            && source.workspace !== repositoryWorkspace && !(await exists(path.join(source.workspace, '.git')));
          if (!repositoryWorkspaceIsActual || !extractedWorkspaceHasNoGit) {
            throw new Error('The interface3 source did not retain separate real and extracted workspaces.');
          }
          const archiveBefore = await snapshot(source.archivePath);
          const inventoryBefore = await snapshot(source.inventoryPath);
          const inventoryBytesBefore = await readFile(source.inventoryPath);
          const inventoryDocument = JSON.parse(inventoryBytesBefore.toString('utf8'));
          const rows = source.inventory.files;
          const exportFirst = await sourceModule.verifyInterface3ServerExport(source);
          const archiveAfterExport = await snapshot(source.archivePath);
          const inventoryAfterExport = await snapshot(source.inventoryPath);
          const inventoryBytesAfterExport = await readFile(source.inventoryPath);
          const actualSource = { ...source, fileCount: source.inventory.fileCount,
            expandedBytes: source.inventory.expandedBytes,
            sourceInventorySha256: source.inventory.sourceInventorySha256 };
          const retained = await sourceModule.verifyRetainedInterface3Archive({ runnerTemp: root,
            runId: String(process.pid), runAttempt: '1', workspace: repositoryWorkspace, evidenceDirectory },
            actualSource, rows);
          const archiveAfterRetained = await snapshot(source.archivePath);
          const inventoryAfterRetained = await snapshot(source.inventoryPath);
          const inventoryBytesAfterRetained = await readFile(source.inventoryPath);
          const exportSecond = await sourceModule.verifyInterface3ServerExport(source);
          const archiveAfterSecondExport = await snapshot(source.archivePath);
          const inventoryAfterSecondExport = await snapshot(source.inventoryPath);
          const inventoryBytesAfterSecondExport = await readFile(source.inventoryPath);
          const exportRevalidated = sameInventory(exportFirst, exportSecond)
            && sameInventory(exportFirst, retained)
            && inventoryDocument.schemaVersion === 1
            && sameRows(rows, inventoryDocument.files)
            && exportFirst.sourceInventorySha256 === source.inventory.sourceInventorySha256;
          const retainedArchiveRevalidated = retained.fileCount === source.inventory.fileCount
            && retained.expandedBytes === source.inventory.expandedBytes
            && retained.sourceInventorySha256 === source.inventory.sourceInventorySha256
            && archiveBefore.size === source.archiveBytes && archiveBefore.sha256 === source.archiveSha256;
          const retainedFilesUnchanged = sameSnapshot(archiveBefore, archiveAfterExport)
            && sameSnapshot(archiveBefore, archiveAfterRetained)
            && sameSnapshot(archiveBefore, archiveAfterSecondExport)
            && sameSnapshot(inventoryBefore, inventoryAfterExport)
            && sameSnapshot(inventoryBefore, inventoryAfterRetained)
            && sameSnapshot(inventoryBefore, inventoryAfterSecondExport)
            && inventoryBytesBefore.equals(inventoryBytesAfterExport)
            && inventoryBytesBefore.equals(inventoryBytesAfterRetained)
            && inventoryBytesBefore.equals(inventoryBytesAfterSecondExport)
            && sha256(inventoryBytesBefore) === source.inventoryFileSha256;
          if (!exportRevalidated || !retainedArchiveRevalidated || !retainedFilesUnchanged) {
            throw new Error('The interface3 source/archive revalidation changed or mismatched retained evidence.');
          }
          result = { Revision: identity.revision, ProofModuleLoaded: proofModuleLoaded,
            RepositoryWorkspaceIsActual: repositoryWorkspaceIsActual, ExtractedWorkspaceHasNoGit: extractedWorkspaceHasNoGit,
            SourceMetadataValid: sourceMetadataValid, ProtocolMetadataValid: protocolMetadataValid,
            ExportRevalidated: exportRevalidated, RetainedArchiveRevalidated: retainedArchiveRevalidated,
            RetainedFilesUnchanged: retainedFilesUnchanged, SourceCleanupComplete: false,
            TestRootCleanupComplete: false, ArchiveName: identity.archiveName,
            InventoryName: identity.inventoryName, FileCount: source.inventory.fileCount,
            ExpandedBytes: source.inventory.expandedBytes, ArchiveBytes: source.archiveBytes };
        } catch (error) { primary = error; }
        const cleanupFailures = [];
        let sourceCleanupComplete = source === undefined;
        if (source) {
          try {
            await sourceModule.cleanupInterface3ServerSource(source);
            sourceCleanupComplete = !(await exists(source.ownedRoot));
          } catch (error) { cleanupFailures.push(error); }
        }
        let testRootCleanupComplete = false;
        try {
          await rm(evidenceDirectory, { recursive: true, force: false });
          await rmdir(root);
          testRootCleanupComplete = !(await exists(root));
        } catch (error) { cleanupFailures.push(error); }
        if (!sourceCleanupComplete || !testRootCleanupComplete) {
          cleanupFailures.push(new Error('The interface3 source probe did not settle every owned directory.'));
        }
        if (primary && cleanupFailures.length > 0) {
          throw new AggregateError([primary, ...cleanupFailures], 'Interface3 source probe and cleanup failed.');
        }
        if (primary) throw primary;
        if (cleanupFailures.length > 0) throw new AggregateError(cleanupFailures, 'Interface3 source probe cleanup failed.');
        result.SourceCleanupComplete = sourceCleanupComplete;
        result.TestRootCleanupComplete = testRootCleanupComplete;
        process.stdout.write(JSON.stringify(result));

        async function snapshot(file) {
          const info = await lstat(file);
          if (!info.isFile() || info.isSymbolicLink()) throw new Error('Expected an actual regular retained artifact.');
          return { dev: String(info.dev), ino: String(info.ino), mode: info.mode, size: info.size, sha256: await fileSha(file) };
        }
        function sameSnapshot(left, right) {
          return left.dev === right.dev && left.ino === right.ino && left.mode === right.mode
            && left.size === right.size && left.sha256 === right.sha256;
        }
        function sameInventory(left, right) {
          return left.fileCount === right.fileCount && left.expandedBytes === right.expandedBytes
            && left.sourceInventorySha256 === right.sourceInventorySha256 && sameRows(left.files, right.files);
        }
        function sameRows(left, right) {
          return Array.isArray(left) && Array.isArray(right) && left.length === right.length
            && left.every((row, index) => row.path === right[index].path && row.mode === right[index].mode
              && row.bytes === right[index].bytes && row.sha256 === right[index].sha256);
        }
        async function fileSha(file) {
          const hash = createHash('sha256');
          for await (const chunk of createReadStream(file)) hash.update(chunk);
          return hash.digest('hex');
        }
        function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
        async function exists(file) {
          try { await lstat(file); return true; }
          catch (error) { if (error?.code === 'ENOENT') return false; throw error; }
        }
        """;

    internal static async Task<Interface3ServerSourceProbeResult> RunAsync(CancellationToken cancellationToken)
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var sourceModule = Path.Combine(root, "scripts", "Features", "ClusterRouting", "interface3-server-source.mjs");
        var proofModule = Path.Combine(root, "scripts", "Features", "ClusterRouting", "interface3-server-proof.mjs");
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", Probe, sourceModule, proofModule], cancellationToken);
        if (result.ExitCode != 0 || result.Error.Length != 0)
        { throw new InvalidOperationException("The interface3 source probe failed: " + result.Error); }
        return JsonSerializer.Deserialize(result.Output, Interface3ServerSourceProbeJsonContext.Default.Interface3ServerSourceProbeResult)
            ?? throw new InvalidOperationException("The interface3 source probe returned invalid JSON.");
    }
}
